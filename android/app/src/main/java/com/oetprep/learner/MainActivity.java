package com.oetprep.learner;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;
import com.oetprep.learner.plugins.PlaybackAttestationPlugin;
import com.oetprep.learner.plugins.SpeakingRecorderPlugin;

public class MainActivity extends BridgeActivity {
	@Override
	public void onCreate(Bundle savedInstanceState) {
		// BridgeActivity.onCreate() builds the Bridge (registers plugins into
		// PluginHeaders and starts loading the WebView) as part of super.onCreate()
		// itself. registerPlugin() only appends to the Bridge.Builder's plugin
		// list, so calling it AFTER super.onCreate() is a no-op on the already-built
		// Bridge — these plugins silently never reach the JS side. Register before
		// super.onCreate(), matching iOS's OETBridgeViewController.capacitorDidLoad().
		registerPlugin(SpeakingRecorderPlugin.class);
		registerPlugin(PlaybackAttestationPlugin.class);
		super.onCreate(savedInstanceState);
	}
}
