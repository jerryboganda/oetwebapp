# Add project specific ProGuard rules here.
# You can control the set of applied configuration files using the
# proguardFiles setting in build.gradle.
#
# For more details, see
#   http://developer.android.com/guide/developing/tools/proguard.html

# If your project uses WebView with JS, uncomment the following
# and specify the fully qualified class name to the JavaScript interface
# class:
#-keepclassmembers class fqcn.of.javascript.interface.for.webview {
#   public *;
#}

# Preserve line number information for debugging stack traces.
-keepattributes SourceFile,LineNumberTable

# Hide the original source file name.
-renamesourcefileattribute SourceFile

# ── Capacitor ────────────────────────────────────────────────────

# Keep Capacitor plugin classes (bridge interface)
-keep class com.getcapacitor.** { *; }
-keep class com.oetprep.learner.** { *; }

# Keep JavaScript interface annotations
-keepclassmembers class * {
    @android.webkit.JavascriptInterface <methods>;
}

# Keep Capacitor plugin annotations
-keepattributes *Annotation*

# ── Firebase / Push Notifications ────────────────────────────────

-keep class com.google.firebase.** { *; }
-keep class com.google.android.gms.** { *; }

# ── SecureStoragePlugin ──────────────────────────────────────────

-keep class com.nickyamanern.** { *; }
