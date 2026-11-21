package com.morsedero.daiseytracker

import android.app.usage.UsageEvents
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * One day's phone use, in the same shape the Windows tray app sends (Summary.cs), so Daisey reads
 * a phone as one more device. Read from Android's usage events (the data Digital Wellbeing shows);
 * nothing but app names and minutes leaves the phone.
 *
 * A day runs 06:00 to 05:59, like the tracker's files and Daisey's own day. Timeline and hourly
 * minutes count from that 06:00.
 */
object Usage {
    const val DAY_START_HOUR = 6
    private const val MIN = 60_000L

    /** The logical day `ms` falls in (before 06:00 belongs to the day before). */
    fun dayOf(ms: Long): LocalDate {
        val t = LocalDateTime.ofInstant(java.time.Instant.ofEpochMilli(ms), ZoneId.systemDefault())
        return if (t.hour < DAY_START_HOUR) t.toLocalDate().minusDays(1) else t.toLocalDate()
    }

    fun startOf(day: LocalDate): Long =
        day.atTime(DAY_START_HOUR, 0).atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()

    /** Foreground spans [start, end) of each package within [from, to). */
    private fun spans(ctx: Context, from: Long, to: Long): List<Triple<String, Long, Long>> {
        val usm = ctx.getSystemService(Context.USAGE_STATS_SERVICE) as UsageStatsManager
        // Look back an hour so an app already open at 06:00 is caught.
        val events = usm.queryEvents(from - 60 * MIN, to)
        val out = ArrayList<Triple<String, Long, Long>>()
        val ignore = ignored(ctx)
        var cur: String? = null
        var since = 0L
        fun close(at: Long) {
            val p = cur ?: return
            val s = maxOf(since, from); val e = minOf(at, to)
            if (e > s && p !in ignore) out.add(Triple(p, s, e))
            cur = null
        }
        val ev = UsageEvents.Event()
        while (events.hasNextEvent()) {
            events.getNextEvent(ev)
            when (ev.eventType) {
                UsageEvents.Event.ACTIVITY_RESUMED -> {
                    if (cur != ev.packageName) { close(ev.timeStamp); cur = ev.packageName; since = ev.timeStamp }
                }
                UsageEvents.Event.ACTIVITY_PAUSED -> if (cur == ev.packageName) close(ev.timeStamp)
                UsageEvents.Event.SCREEN_NON_INTERACTIVE,
                UsageEvents.Event.KEYGUARD_SHOWN,
                UsageEvents.Event.DEVICE_SHUTDOWN -> close(ev.timeStamp)
            }
        }
        close(to) // still open now
        return out
    }

    /** The home screen, the system UI and this app don't count as use. */
    private fun ignored(ctx: Context): Set<String> {
        val home = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
        val launchers = ctx.packageManager.queryIntentActivities(home, 0).map { it.activityInfo.packageName }
        return (launchers + listOf(ctx.packageName, "com.android.systemui")).toSet()
    }

    private fun label(pm: PackageManager, pkg: String): String = try {
        pm.getApplicationLabel(pm.getApplicationInfo(pkg, 0)).toString().take(60)
    } catch (_: Exception) { pkg.substringAfterLast('.').replaceFirstChar { it.uppercase() } }

    private fun r2(v: Double) = Math.round(v * 100) / 100.0

    /** The summary for `day`, up to now (or the day's end once it's over). */
    fun summary(ctx: Context, day: LocalDate, now: Long = System.currentTimeMillis()): JSONObject {
        val start = startOf(day)
        val end = minOf(now, startOf(day.plusDays(1)))
        val pm = ctx.packageManager
        val names = HashMap<String, String>()
        fun name(p: String) = names.getOrPut(p) { label(pm, p) }

        val apps = HashMap<String, Double>()
        val hourly = DoubleArray(24)
        val minutes = (((end - start) / MIN).toInt() + 1).coerceIn(0, 1500)
        val perMinute = arrayOfNulls<HashMap<String, Long>>(minutes)

        for ((pkg, s, e) in spans(ctx, start, end)) {
            val app = name(pkg)
            apps[app] = (apps[app] ?: 0.0) + (e - s) / MIN.toDouble()
            var t = s
            while (t < e) {
                val h = ((t - start) / (60 * MIN)).toInt().coerceIn(0, 23)
                val hourEnd = if (h == 23) e else minOf(e, start + (h + 1) * 60 * MIN)
                hourly[h] += (hourEnd - t) / MIN.toDouble()
                t = hourEnd
            }
            t = s
            while (t < e) {
                val mi = ((t - start) / MIN).toInt()
                if (mi >= minutes) break
                val minEnd = minOf(e, start + (mi + 1) * MIN)
                val m = perMinute[mi] ?: HashMap<String, Long>().also { perMinute[mi] = it }
                m[app] = (m[app] ?: 0L) + (minEnd - t)
                t = minEnd
            }
        }

        // Each minute goes to the app used most in it (20 s or more, else nothing), merged into runs.
        val timeline = JSONArray()
        var run: String? = null
        var runStart = 0
        for (mi in 0..minutes) {
            val top = if (mi < minutes) perMinute[mi]?.maxByOrNull { it.value }?.takeIf { it.value >= 20_000 }?.key else null
            if (top == run) continue
            if (run != null) timeline.put(JSONArray().put(runStart).put(mi).put(run).put(JSONObject.NULL))
            run = top; runStart = mi
        }

        val appJson = JSONObject()
        apps.entries.sortedByDescending { it.value }.take(300).forEach { appJson.put(it.key, r2(it.value)) }
        return JSONObject()
            .put("date", day.toString())
            .put("dayStartMinute", DAY_START_HOUR * 60)
            .put("activeMinutes", r2(apps.values.sum()))
            .put("appMinutes", appJson)
            .put("siteMinutes", JSONObject())
            .put("detailMinutes", JSONObject())
            .put("timeline", timeline)
            .put("hourlyMinutes", JSONArray(hourly.map { Math.round(it * 10) / 10.0 }))
    }
}
