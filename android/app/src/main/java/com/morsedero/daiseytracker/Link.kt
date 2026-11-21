package com.morsedero.daiseytracker

import android.content.Context
import android.os.Build
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Link to the user's Daisey, the same way the Windows tray app does (DaiseyLink.cs): `start` gets
 * a request id, Daisey opens at ?tracker=<req> for the user to Approve, `poll` hands the device
 * token over once. After that, `upload` sends a day's summary with that token.
 */
object Link {
    private const val SITE = "https://morsedero.com"
    private const val API = "$SITE/.netlify/functions/daisey-now-tracker"
    const val APP_URL = "$SITE/daisey/now/"
    private const val PREFS = "daisey-link"

    private fun prefs(ctx: Context) = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
    fun token(ctx: Context): String? = prefs(ctx).getString("token", null)
    fun linked(ctx: Context) = token(ctx) != null
    fun forget(ctx: Context) = prefs(ctx).edit().clear().apply()
    fun lastSent(ctx: Context): Long = prefs(ctx).getLong("sentAt", 0)

    /** Shown on Daisey's Approve screen and in its device list. */
    val deviceName: String
        get() = "${Build.MANUFACTURER.replaceFirstChar { it.uppercase() }} ${Build.MODEL} (phone)".take(60)

    class Http(val code: Int, val body: JSONObject)

    private fun post(body: JSONObject, token: String? = null): Http {
        val c = URL(API).openConnection() as HttpURLConnection
        try {
            c.requestMethod = "POST"
            c.connectTimeout = 20_000; c.readTimeout = 30_000
            c.doOutput = true
            c.setRequestProperty("Content-Type", "application/json")
            if (token != null) c.setRequestProperty("Authorization", "Bearer $token")
            c.outputStream.use { it.write(body.toString().toByteArray()) }
            val code = c.responseCode
            val text = (if (code < 400) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: "{}"
            return Http(code, runCatching { JSONObject(text) }.getOrDefault(JSONObject()))
        } finally { c.disconnect() }
    }

    /** A link request; open APP_URL?tracker=<req> for the user to approve. */
    fun start(): String? = post(JSONObject().put("action", "start").put("device", deviceName))
        .takeIf { it.code == 200 }?.body?.optString("req")?.takeIf { it.isNotEmpty() }

    /** "pending", "approved" (token saved) or "gone". */
    fun poll(ctx: Context, req: String): String {
        val r = post(JSONObject().put("action", "poll").put("req", req))
        val status = r.body.optString("status", "pending")
        if (status == "approved") {
            val t = r.body.optString("token")
            if (t.startsWith("dtk_")) prefs(ctx).edit().putString("token", t).apply() else return "gone"
        }
        return status
    }

    /** True when Daisey has it. A token Daisey no longer knows (disconnected there) is forgotten. */
    fun upload(ctx: Context, day: JSONObject): Boolean {
        val t = token(ctx) ?: return false
        val r = post(JSONObject().put("action", "upload").put("day", day), t)
        if (r.code == 401) { forget(ctx); return false }
        if (r.code == 200) prefs(ctx).edit().putLong("sentAt", System.currentTimeMillis()).apply()
        return r.code == 200
    }

    /** The last day sent in full (after it ended), so a finished day is sent once more, complete. */
    fun lastFinal(ctx: Context): String? = prefs(ctx).getString("lastFinal", null)
    fun setLastFinal(ctx: Context, date: String) = prefs(ctx).edit().putString("lastFinal", date).apply()
}
