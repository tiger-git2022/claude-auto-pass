package com.personal.claudeautopass;
import android.content.*;
public final class ToggleReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context c, Intent intent) {
        if (Control.ACTION_TOGGLE.equals(intent.getAction())) Control.setEnabled(c, !Control.enabled(c));
        else if (Control.ACTION_EXIT.equals(intent.getAction())) Control.exit(c);
    }
}
