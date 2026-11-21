package com.morsedero.daiseytracker

import android.app.AppOpsManager
import android.content.Context
import android.os.Process
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import java.util.concurrent.TimeUnit

/**
 * Every 15 minutes (Android may stretch that while the phone sleeps; each send covers the whole
 * day so far, so nothing is lost): today's summary, and yesterday's once more, complete, the
 * first time after it ended.
 */
class UploadWorker(ctx: Context, params: WorkerParameters) : Worker(ctx, params) {
    override fun doWork(): Result {
        val ctx = applicationContext
        if (!Link.linked(ctx) || !hasUsageAccess(ctx)) return Result.success()
        return try {
            val now = System.currentTimeMillis()
            val today = Usage.dayOf(now)
            val yesterday = today.minusDays(1).toString()
            if (Link.lastFinal(ctx) != yesterday) {
                if (Link.upload(ctx, Usage.summary(ctx, today.minusDays(1), now))) Link.setLastFinal(ctx, yesterday)
            }
            Link.upload(ctx, Usage.summary(ctx, today, now))
            Result.success()
        } catch (_: Exception) {
            Result.retry()
        }
    }

    companion object {
        private const val NAME = "daisey-upload"
        private val online = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()

        fun schedule(ctx: Context) {
            val req = PeriodicWorkRequestBuilder<UploadWorker>(15, TimeUnit.MINUTES).setConstraints(online).build()
            WorkManager.getInstance(ctx).enqueueUniquePeriodicWork(NAME, ExistingPeriodicWorkPolicy.KEEP, req)
        }

        fun sendNow(ctx: Context) {
            WorkManager.getInstance(ctx).enqueue(OneTimeWorkRequestBuilder<UploadWorker>().setConstraints(online).build())
        }

        fun hasUsageAccess(ctx: Context): Boolean {
            val ops = ctx.getSystemService(Context.APP_OPS_SERVICE) as AppOpsManager
            @Suppress("DEPRECATION")
            val mode = ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), ctx.packageName)
            return mode == AppOpsManager.MODE_ALLOWED
        }
    }
}
