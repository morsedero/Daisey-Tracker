package com.morsedero.daiseytracker

import android.app.Activity
import android.content.Intent
import android.graphics.Typeface
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.Gravity
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import java.text.DateFormat
import java.util.Date

/**
 * One screen: allow usage access, connect to Daisey, see when it last sent. Everything after that
 * runs in the background (UploadWorker).
 */
class MainActivity : Activity() {
    private lateinit var status: TextView
    private lateinit var access: Button
    private lateinit var connect: Button
    private lateinit var send: Button
    private lateinit var forget: Button
    private val main = Handler(Looper.getMainLooper())
    @Volatile private var linking = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val pad = (24 * resources.displayMetrics.density).toInt()
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(pad, pad * 2, pad, pad); gravity = Gravity.TOP }
        root.addView(TextView(this).apply { text = "Daisey Tracker"; textSize = 26f; setTypeface(typeface, Typeface.BOLD) })
        root.addView(TextView(this).apply {
            text = "Sends which apps you use on this phone, and for how long, to your Daisey. Nothing else: no messages, no screen content."
            textSize = 15f; setPadding(0, pad / 2, 0, pad)
        })
        status = TextView(this).apply { textSize = 16f; setPadding(0, 0, 0, pad) }
        root.addView(status)
        access = Button(this).apply { text = "Allow usage access"; setOnClickListener { startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)) } }
        connect = Button(this).apply { text = "Connect to Daisey"; setOnClickListener { link() } }
        send = Button(this).apply { text = "Send now"; setOnClickListener { UploadWorker.sendNow(this@MainActivity); say("Sending…") } }
        forget = Button(this).apply {
            text = "Disconnect"
            setOnClickListener { Link.forget(this@MainActivity); refresh(); say("Disconnected here. Remove the phone in Daisey's Activity screen to drop its data.") }
        }
        listOf(access, connect, send, forget).forEach { root.addView(it) }
        setContentView(root)
    }

    override fun onResume() { super.onResume(); refresh() }

    private fun say(s: String) { status.text = s }

    private fun refresh() {
        val ok = UploadWorker.hasUsageAccess(this)
        val linked = Link.linked(this)
        access.isEnabled = !ok
        access.text = if (ok) "Usage access allowed" else "Allow usage access"
        connect.isEnabled = ok && !linked && !linking
        send.isEnabled = ok && linked
        forget.isEnabled = linked
        if (linked) UploadWorker.schedule(this)
        if (!linking) say(when {
            !ok -> "Step 1: allow usage access for Daisey Tracker."
            !linked -> "Step 2: connect to Daisey."
            Link.lastSent(this) > 0 -> "Connected. Last sent ${DateFormat.getTimeInstance(DateFormat.SHORT).format(Date(Link.lastSent(this)))}."
            else -> "Connected. First send within 15 minutes."
        })
    }

    /** start → open Daisey to Approve → poll until the token comes (15 min at most, like the server). */
    private fun link() {
        linking = true; refresh(); say("Opening Daisey… tap Approve there, then come back.")
        Thread {
            val req = runCatching { Link.start() }.getOrNull()
            if (req == null) { main.post { linking = false; refresh(); say("Couldn't reach Daisey. Check the connection and try again.") }; return@Thread }
            main.post { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(Link.APP_URL + "?tracker=" + Uri.encode(req)))) }
            val until = System.currentTimeMillis() + 15 * 60_000
            var result = "pending"
            while (result == "pending" && System.currentTimeMillis() < until) {
                Thread.sleep(3000)
                result = runCatching { Link.poll(this, req) }.getOrDefault("pending")
            }
            main.post {
                linking = false
                if (result == "approved") { UploadWorker.schedule(this); UploadWorker.sendNow(this) }
                refresh()
                if (result != "approved") say("Not approved in time. Tap Connect to try again.")
            }
        }.start()
    }
}
