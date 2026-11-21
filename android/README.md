# Daisey Tracker for Android

The phone side of Daisey Tracker. Reads Android's usage events (the same data Digital
Wellbeing shows) and sends Daisey which apps were used and for how long, in the same
day-summary shape as the Windows app (`src/DaiseyTracker/Summary.cs`), through the same
`daisey-now-tracker` link/upload protocol. Daisey sees the phone as one more device.

- **Install:** `DaiseyTracker.apk` from the `android-latest` release (not "Latest": that
  one belongs to the Windows app's auto-update). Allow installing from the browser once.
- **Setup:** open it → Allow usage access (Settings) → Connect to Daisey → Approve in Daisey.
- **Then:** every ~15 min in the background (WorkManager); Android may stretch that in
  Doze, and each send covers the whole day so far. Yesterday is re-sent once, complete.
- **Privacy:** app names and minutes only. No window/screen content, no messages.
- **Build:** `.github/workflows/android.yml` (Gradle 8.7, JDK 17). Without the
  `ANDROID_KEYSTORE_*` secrets the APK is debug-signed, so a new build may need the old
  one uninstalled first; add a keystore to update in place.
