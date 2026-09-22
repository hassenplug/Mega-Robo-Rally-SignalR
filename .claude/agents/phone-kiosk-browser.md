---
name: phone-kiosk-browser
description: >
  Knowledge base for a new, separate project: an Android kiosk-browser APK that
  locks one of MRR's 6 player phones into full-screen, single-purpose display of
  the MRR player UI (index.html) -- no status bar, no way to back out to the
  home screen or another app, auto-launches on boot. Not part of the MRR C#
  codebase; this is Android/Kotlin. Use whenever building, debugging, or
  provisioning that kiosk-browser app. Documents the reference implementation
  (source verified against GitHub, in full below), the Android APIs it depends
  on (Device Owner, Lock Task/screen pinning), the ADB provisioning flow, and
  what has to change to point it at MRR instead of the reference author's blog.
model: sonnet
tools:
  - Read
  - Write
  - Edit
  - Bash
  - Glob
  - Grep
  - Agent
---

# Phone Kiosk Browser — Android Reference Agent

## 0. Status: knowledge base only, no code written yet

This file was created 2026-09-22 to capture reference material before any
implementation starts. **No Android project exists in this repo for it.** It is
a genuinely separate project from the MRR C#/ASP.NET codebase -- Kotlin,
Android Gradle, its own APK -- kept here for now only because this is the
repo open when the reference material was gathered. Decide before writing code
whether it gets its own repo/directory; if so, move this file there and drop
it from `CLAUDE.md`'s Active Agents table.

**Why MRR wants this:** six player phones (`CLAUDE.md`'s Hardware table) run
[MRR/wwwroot/index.html](../../MRR/wwwroot/index.html) in an ordinary mobile
browser today. At a live event, nothing stops a player from swiping to the
home screen, opening another app, or getting interrupted by a notification
mid-turn. A kiosk-mode APK would pin the phone to exactly that one page,
auto-launching on boot, with no way out short of deliberately unprovisioning
the device.

---

## 1. Source of truth

Blog post (the URL the user supplied, an archived copy):
<https://web.archive.org/web/20220119130012/https://sisik.eu/blog/android/dev-admin/kiosk-browser>
(the live site returned HTTP 509 "Bandwidth Limit Exceeded" and the archive.org
copy could not be fetched from this session either -- both attempts failed;
what follows was instead pulled directly from the reference implementation's
actual GitHub repo, which is more useful than the blog prose anyway).

**GitHub repo:** <https://github.com/sixo/kiosk-browser> (Apache-2.0, by Roman
Sisik, same author as the blog). Verified via the GitHub API 2026-09-22 --
12 stars, 5 forks, last content matches what's transcribed below file-for-file
via `raw.githubusercontent.com` (not an AI paraphrase of the page).

**This reference is from 2019 and will not build as-is against a current
Android toolchain** -- see §5 "What has to change" before starting real work.

---

## 2. The reference implementation, verbatim

Package `eu.sisik.kioskbrowser`. Kotlin, `com.android.support` (pre-AndroidX),
Kotlin synthetics for view binding, compileSdk/targetSdk 28, AGP 3.3.2,
Kotlin 1.3.21, minSdk 22.

### `app/src/main/AndroidManifest.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
          package="eu.sisik.kioskbrowser">

    <uses-permission android:name="android.permission.INTERNET"/>

    <application
            android:testOnly="true"
            android:allowBackup="true"
            android:icon="@mipmap/ic_launcher"
            android:label="@string/app_name"
            android:roundIcon="@mipmap/ic_launcher_round"
            android:supportsRtl="true"
            android:theme="@style/AppTheme">

        <activity android:name=".MainActivity">
            <intent-filter>
                <action android:name="android.intent.action.MAIN"/>

                <category android:name="android.intent.category.HOME" />
                <category android:name="android.intent.category.DEFAULT" />

                <category android:name="android.intent.category.LAUNCHER"/>
            </intent-filter>
        </activity>

        <receiver
                android:name="eu.sisik.kioskbrowser.DevAdminReceiver"
                android:label="@string/app_name"
                android:permission="android.permission.BIND_DEVICE_ADMIN" >
            <intent-filter>
                <action android:name="android.app.action.DEVICE_ADMIN_ENABLED" />
            </intent-filter>

            <meta-data
                    android:name="android.app.device_admin"
                    android:resource="@xml/device_admin" />
        </receiver>

        <receiver android:name="eu.sisik.kioskbrowser.BootCompletedReceiver">
            <intent-filter >
                <action android:name="android.intent.action.BOOT_COMPLETED"/>
            </intent-filter>
        </receiver>

    </application>

</manifest>
```

The `HOME` category on `MainActivity` is what lets this app stand in for the
launcher; combined with Lock Task mode (below) that's what makes the home
button a no-op instead of backing out to the real launcher.
`android:testOnly="true"` is a leftover from the reference author's own build
setup -- drop it for a real build (a `testOnly` APK refuses to install outside
a dev/CI context on some devices).

### `app/src/main/java/eu/sisik/kioskbrowser/MainActivity.kt`

```kotlin
package eu.sisik.kioskbrowser

import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.support.v7.app.AppCompatActivity
import android.os.Bundle
import android.os.Handler
import android.util.Log
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
import kotlinx.android.synthetic.main.activity_main.*

class MainActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        checkIfDeviceOwner()
        initWebView()
    }

    override fun onResume() {
        super.onResume()

        tryToStartLockTask()
    }

    private fun checkIfDeviceOwner() {

        val dpm = getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager

        if (!dpm.isDeviceOwnerApp(packageName)) {
            Toast.makeText(this, getString(R.string.err_not_device_owner), Toast.LENGTH_LONG).show()
            finish()
        }
    }

    private fun initWebView() {

        webView.webViewClient = KioskWebViewClient()
        webView.loadUrl(MY_URL)
    }

    private fun tryToStartLockTask() {
        try {

            val dpm = getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
            if (dpm.isDeviceOwnerApp(packageName)) {
                // Allow locktask for my app if not already allowed
                if (!dpm.isLockTaskPermitted(packageName)) {
                    val cn = ComponentName(this, DevAdminReceiver::class.java!!)
                    dpm?.setLockTaskPackages(cn, arrayOf(packageName))
                }

                startLockTask()

            } else {
                Toast.makeText(this, getString(R.string.err_locktask_not_permitted), Toast.LENGTH_LONG).show()
                finish()
            }

        } catch (e: Exception) {

            // Cannot start locktask... try a bit later
            Handler(mainLooper).postDelayed({

                tryToStartLockTask()
            }, 2000)
        }
    }

    private fun hideApps() {

        val dpm = getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager?

        val packagesToHide = arrayOf(
            "com.android.launcher3",
            "some.other.package"
        )

        for (pkg in packagesToHide)
            dpm?.setApplicationHidden(componentName, pkg, false)
    }


    // WebViewClient will handle the blacklisting/whitelisting of URLs
    internal class KioskWebViewClient : WebViewClient() {

        // Whitelisted hosts
        private val allowedHosts = arrayOf(
            "sisik.eu",
            "www.sisik.eu"
        )

        override fun shouldOverrideUrlLoading(view: WebView?, request: WebResourceRequest?): Boolean {

            // Allow to load only whitelisted hosts
            if (allowedHosts.contains(request?.url?.host))
                return false

            // Not allowed to load this url - do nothing..
            return true
        }
    }


    companion object {

        // This is the page that will be loaded by kiosk app
        private const val MY_URL = "https://sisik.eu"
    }
}
```

`hideApps()` is defined but **never called** in this reference -- it's shown
in the blog as an optional extra, not part of the working flow. Note it's also
buggy as written: `setApplicationHidden(componentName, pkg, false)` passes
`false` for "hidden", which *un*-hides rather than hides; would need `true` to
actually work, and only affects OTHER apps the device owner manages, not
system UI elements.

### `app/src/main/java/eu/sisik/kioskbrowser/DeviceAdminReceiver.kt`

```kotlin
package eu.sisik.kioskbrowser

import android.app.admin.DeviceAdminReceiver
import android.app.admin.DevicePolicyManager
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.util.Log
import java.lang.Exception

/**
 * Copyright (c) 2019 by Roman Sisik. All rights reserved.
 */
class DevAdminReceiver: DeviceAdminReceiver() {
    override fun onEnabled(context: Context?, intent: Intent?) {
        super.onEnabled(context, intent)
        Log.d(TAG, "Device Owner Enabled")

        try {
            makeLockTaskPackage(context)
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun makeLockTaskPackage(context: Context?) {

        val dpm = context?.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager?
        val cn = ComponentName(context, DevAdminReceiver::class.java!!)
        dpm?.setLockTaskPackages(cn, arrayOf(context?.packageName))
    }

    companion object {
        val TAG = "DevAdminReceiver"
    }
}
```

Note the class file is named `DeviceAdminReceiver.kt` but the class inside is
`DevAdminReceiver` (matches the manifest's `.DevAdminReceiver` reference) --
not a transcription error, that's really how the upstream repo has it.

### `app/src/main/java/eu/sisik/kioskbrowser/BootCompletedReceiver.kt`

```kotlin
package eu.sisik.kioskbrowser

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Copyright (c) 2019 by Roman Sisik. All rights reserved.
 */
class BootCompletedReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {

        val i = Intent(context, MainActivity::class.java)
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)

        context.startActivity(i)
    }
}
```

### `app/src/main/res/xml/device_admin.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<device-admin xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-policies>
    </uses-policies>
</device-admin>
```

Empty `<uses-policies>` -- this app claims *no* device-admin policies at all
(no password requirements, no camera disable, etc.). All of the actual
lock-down power here comes from being the **Device Owner**, not from anything
declared in this file.

### `app/src/main/res/layout/activity_main.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<FrameLayout
        xmlns:android="http://schemas.android.com/apk/res/android"
        xmlns:tools="http://schemas.android.com/tools"
        android:layout_width="match_parent"
        android:layout_height="match_parent"
        tools:context=".MainActivity">

    <WebView
            android:id="@+id/webView"
            android:layout_width="match_parent"
            android:layout_height="match_parent"/>

</FrameLayout>
```

### `app/src/main/res/values/strings.xml`

```xml
<resources>
    <string name="app_name">KioskBrowser</string>
    <string name="err_not_device_owner">App is not a device owner!</string>
    <string name="err_locktask_not_permitted">App is not allowed to use lock task!!</string>
</resources>
```

### `app/build.gradle`

```groovy
apply plugin: 'com.android.application'

apply plugin: 'kotlin-android'

apply plugin: 'kotlin-android-extensions'

android {
    compileSdkVersion 28
    defaultConfig {
        applicationId "eu.sisik.kioskbrowser"
        minSdkVersion 22
        targetSdkVersion 28
        versionCode 1
        versionName "1.0"
        testInstrumentationRunner "android.support.test.runner.AndroidJUnitRunner"
    }
    buildTypes {
        release {
            minifyEnabled false
            proguardFiles getDefaultProguardFile('proguard-android-optimize.txt'), 'proguard-rules.pro'
        }
    }
}

dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    implementation"org.jetbrains.kotlin:kotlin-stdlib-jdk7:$kotlin_version"
    implementation 'com.android.support:appcompat-v7:28.0.0'
    implementation 'com.android.support.constraint:constraint-layout:1.1.3'
    testImplementation 'junit:junit:4.12'
    androidTestImplementation 'com.android.support.test:runner:1.0.2'
    androidTestImplementation 'com.android.support.test.espresso:espresso-core:3.0.2'
}
```

### Provisioning (from the README)

```
adb shell dpm set-device-owner eu.sisik.kioskbrowser/.DevAdminReceiver
```

Device Owner can only be set via ADB on a device with **no accounts ever
configured** -- fresh out of the box or right after a factory reset. This is
the single biggest deployment friction point for reusing already-set-up event
phones (§5).

---

## 3. The Android concepts this depends on

- **Device Owner** (`DevicePolicyManager.isDeviceOwnerApp`): the strongest
  local management role a normal (non-managed-fleet) Android device supports.
  Granted once, via ADB, only on a device with no accounts. Lets the app call
  `setLockTaskPackages` and other `DevicePolicyManager` APIs no ordinary app
  can call.
- **Lock Task Mode / screen pinning** (`Activity.startLockTask()`,
  `DevicePolicyManager.setLockTaskPackages`): pins the current activity so the
  home/recents buttons and the notification shade pull-down are suppressed
  while it's active. A Device Owner app can whitelist itself for this and
  start it unprompted (`startLockTask()` with no user confirmation dialog);
  without Device Owner, a user can still manually "pin" any app via Android's
  Settings > Security > Screen pinning, but that requires the user's own
  action every time and can be exited with a long-press-back gesture.
- **`DeviceAdminReceiver`**: the required receiver a Device Owner app must
  declare (`BIND_DEVICE_ADMIN` permission, `device_admin.xml` meta-data). Its
  `onEnabled()` fires once, right when `dpm set-device-owner` succeeds --
  the reference app uses that moment to self-whitelist for lock task so
  `MainActivity` doesn't have to race for permission on first launch.
- **`HOME` intent-category + `BOOT_COMPLETED` receiver**: together these make
  the app act as a persistent kiosk launcher -- `HOME` lets it stand in for
  the real launcher (so pressing Home, if it ever briefly worked, lands back
  here instead of the real launcher), and `BOOT_COMPLETED` auto-starts it after
  every reboot/power-cycle without anyone unlocking the phone and tapping an
  icon first.
- **`WebViewClient.shouldOverrideUrlLoading`**: the *only* navigation
  restriction in this app -- an allow-list of hostnames a link/redirect is
  permitted to load. Everything else silently does nothing (returns `true` =
  "handled, don't navigate"). This is purely a same-app-different-URL guard;
  it has nothing to do with Lock Task and doesn't stop the user leaving the
  app some other way (that's Lock Task's job).

---

## 4. What has to change to point this at MRR

None of this exists yet -- listed in the order it'd actually need doing:

1. **Modernize the toolchain first**, or this literally will not open in a
   current Android Studio without a wall of deprecation/removal errors:
   - AndroidX (`androidx.appcompat.app.AppCompatActivity`) instead of
     `android.support.v7`/`com.android.support.constraint` -- the support
     library is long dead.
   - `kotlin-android-extensions` (the `kotlinx.android.synthetic.*` import) was
     **removed** from the Kotlin Gradle plugin years ago; replace with View
     Binding (`ActivityMainBinding`) or `findViewById`.
   - Current AGP/Gradle/Kotlin versions; `compileSdk`/`targetSdk` far higher
     than 28 (Play Store minimums move every year -- check current
     requirements if this is ever distributed that way; for ADB-sideloaded
     kiosk devices only, target-sdk compliance mostly doesn't matter, but the
     *compile*-sdk still needs to be recent enough for current AGP).
   - `applicationId`/package `eu.sisik.kioskbrowser` is the original author's
     -- needs its own (e.g. `com.eii.mrrkiosk` or similar), and the manifest's
     receiver names change with it.
2. **Enable JavaScript and DOM storage on the `WebView`.** The reference app
   never touches `WebView.settings` at all, meaning JavaScript is **disabled**
   by default. MRR's player UI (`js/loadrobots.js`) is entirely JS-driven --
   SignalR over WebSocket, `document.cookie` for the seat-login cookie
   (`js/loadrobots.js`'s `LOGIN_COOKIE`) -- none of it runs without:
   ```kotlin
   webView.settings.javaScriptEnabled = true
   webView.settings.domStorageEnabled = true
   ```
   This is the one change in this whole list that is a hard functional
   requirement, not a nice-to-have -- skip it and the phone just shows a blank
   or broken page.
3. **Point `MY_URL` at the Pi, not the reference author's blog.** Per
   `CLAUDE.md`'s own rule ("Server hostname: `mrobopi`. Never hardcode it"),
   don't bake `http://mrobopi:5000/` into the APK as a compile-time constant
   the way this reference bakes in `https://sisik.eu` -- every one of the 6
   phones loads the exact same URL (there's no per-phone identity anymore;
   `js/loadrobots.js`'s seat-login cookie is what tells them apart, not the
   URL), so a single misconfigured build would mis-point all 6 at once.
   Prefer a value settable without a rebuild: a Device Owner managed
   configuration key, an ADB `am start` extra read on first launch and cached,
   or at minimum a same-network mDNS/broadcast discovery step -- **not yet
   decided, flag for discussion before implementing.**
4. **Update `KioskWebViewClient`'s `allowedHosts`** to whatever hostname the
   Pi actually answers to on the game network (`mrobopi`, or its IP -- see
   `install/todo.md` Section 5's network setup, still mostly unbuilt as of
   2026-09-18) instead of `sisik.eu`/`www.sisik.eu`.
5. **Consider hiding the status bar too.** Lock Task alone still leaves the
   system status bar visible (network/battery icons, pull-down shade blocked
   but bar itself shown) unless the activity also goes immersive
   (`WindowInsetsController.hide(WindowInsets.Type.systemBars())` on API 30+,
   or the older `View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY` for lower API levels).
   Not present in the reference at all -- purely a polish decision for how
   kiosk-like the phones should look.
6. **Decide what "leaving kiosk mode" looks like for setup/updates.** This
   reference has no exit mechanism whatsoever once Device Owner + Lock Task
   are both active (by design -- that's the point of a kiosk). Updating the
   APK is still possible via `adb install -r` while a debug cable is attached,
   but there's no in-app way out for reconfiguring the target URL, checking
   Wi-Fi, etc. -- worth deciding whether to build one (e.g. a long-press
   gesture that calls `stopLockTask()`, gated behind something only staff
   would know) before this ships to 6 unattended devices at an event.

---

## 5. Deployment friction and open questions (flag before building)

- **Device Owner requires a clean device** (no Google/other accounts ever
  added). If the 6 event phones have already been used normally, each one
  needs a factory reset first. Confirm this is acceptable for however these
  phones are sourced/managed before committing to the Device-Owner approach
  over the weaker "user manually pins the app via Settings" alternative.
- **Single URL for all 6 phones** — confirmed reasonable given the current
  seat-login design (`js/loadrobots.js`'s `mrr_seat` cookie identifies the
  player, not the phone/URL) — but the mechanism for setting that URL without
  a hardcoded per-build constant is still undecided (§4 item 3).
- **License**: the reference repo is Apache-2.0. Using its code/structure as
  a starting point is fine under that license (attribution + license notice
  requirements apply if source is reused substantially) -- keep the
  `LICENSE` file/attribution if the reference code is copied rather than
  written fresh from the concepts alone.
- **Not yet decided:** where this project's actual code will live (its own
  repo vs. a subdirectory here) — see §0.
