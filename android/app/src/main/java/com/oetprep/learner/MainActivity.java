package com.oetprep.learner;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;
import com.oetprep.learner.plugins.SpeakingRecorderPlugin;

public class MainActivity extends BridgeActivity {
	@Override
	public void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);
		registerPlugin(SpeakingRecorderPlugin.class);
	}
}
