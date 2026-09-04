package com.oetwithdrhesham.app.plugins;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.annotation.CapacitorPlugin;

import org.json.JSONObject;

/**
 * Reports how this APK was installed — Google Play or sideloaded.
 *
 * Why this exists: Play App Signing re-signs the uploaded AAB with the Play
 * app-signing key, which differs from the upload key used for the
 * direct-download APK on the VPS release feed. Android treats those as
 * different apps for update purposes: installing one over the other always
 * fails with "App not installed", and no app-side code can override OS
 * signature enforcement. The only permanent fix is to never cross the
 * channels — the update UI (see lib/mobile/install-source.ts and
 * app/get-app/android-install) routes Play-installed copies to the Play
 * listing and sideloaded copies to the direct APK.
 */
@CapacitorPlugin(name = "InstallerSource")
public class InstallerSourcePlugin extends Plugin {
    private static final String PLAY_INSTALLER = "com.android.vending";

    @com.getcapacitor.PluginMethod
    public void getInstallSource(PluginCall call) {
        Context context = getContext();
        String packageName = context.getPackageName();

        String installer = null;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                installer = context.getPackageManager()
                        .getInstallSourceInfo(packageName)
                        .getInstallingPackageName();
            } else {
                // Deprecated in API 30 but the only option below it; our
                // minSdk predates R so both branches stay.
                installer = context.getPackageManager().getInstallerPackageName(packageName);
            }
        } catch (PackageManager.NameNotFoundException e) {
            call.reject("package not found");
            return;
        }

        int versionCode;
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                versionCode = (int) context.getPackageManager()
                        .getPackageInfo(packageName, 0)
                        .getLongVersionCode();
            } else {
                versionCode = context.getPackageManager()
                        .getPackageInfo(packageName, 0)
                        .versionCode;
            }
        } catch (PackageManager.NameNotFoundException e) {
            call.reject("package not found");
            return;
        }

        JSObject result = new JSObject();
        result.put("installerPackage", installer == null ? JSONObject.NULL : installer);
        result.put("isPlayInstalled", PLAY_INSTALLER.equals(installer));
        result.put("versionCode", versionCode);
        call.resolve(result);
    }
}
