package com.oetwithdrhesham.learner;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;
import com.oetwithdrhesham.learner.plugins.PlaybackAttestationPlugin;
import com.oetwithdrhesham.learner.plugins.SpeakingRecorderPlugin;

public class MainActivity extends BridgeActivity {
	@Override
	public void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);
		registerPlugin(SpeakingRecorderPlugin.class);
		registerPlugin(PlaybackAttestationPlugin.class);
	}
}
