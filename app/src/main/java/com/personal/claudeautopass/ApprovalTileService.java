package com.personal.claudeautopass;
import android.app.KeyguardManager;
import android.service.quicksettings.*;
public final class ApprovalTileService extends TileService {
    @Override public void onStartListening() { update(); }
    @Override public void onClick() {
        super.onClick();
        if (getSystemService(KeyguardManager.class).isKeyguardLocked()) { unlockAndRun(this::toggle); return; }
        toggle();
    }
    private void toggle() { Control.setEnabled(this, !Control.enabled(this)); update(); }
    private void update() {
        Tile tile = getQsTile();
        if (tile == null) return;
        boolean connected = ApprovalService.connected();
        tile.setState(!connected ? Tile.STATE_UNAVAILABLE : Control.enabled(this) ? Tile.STATE_ACTIVE : Tile.STATE_INACTIVE);
        tile.setLabel(connected ? "Claude 自动审批" : "请先开启无障碍");
        tile.updateTile();
    }
}
