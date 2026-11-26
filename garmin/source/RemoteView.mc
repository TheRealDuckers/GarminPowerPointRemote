using Toybox.Graphics;
using Toybox.Lang;
using Toybox.System;
using Toybox.WatchUi;

const VIEW_GAP = 4;
const VIEW_FLASH_MS = 200;
const VIEW_HINT_MS = 2500;
const NO_FLASH = -1;

class RemoteView extends WatchUi.View {
    private var _flashZone = NO_FLASH;
    private var _flashUntil = null;
    private var _hintText = null;
    private var _hintUntil = null;
    private var _shownSecs = -1;

    function initialize() {
        View.initialize();
    }

    // Called by the app's single ticker. Expires the flash/hint and refreshes the talk timer.
    function tick(now) {
        var dirty = false;

        if (_flashUntil != null && (now - _flashUntil) >= 0) {
            _flashUntil = null;
            _flashZone = NO_FLASH;
            dirty = true;
        }
        if (_hintUntil != null && (now - _hintUntil) >= 0) {
            _hintUntil = null;
            _hintText = null;
            dirty = true;
        }

        var start = getApp().sessionStart;
        if (start != null) {
            var secs = (now - start) / 1000;
            if (secs != _shownSecs) {
                _shownSecs = secs;
                dirty = true;
            }
        }

        if (dirty) {
            WatchUi.requestUpdate();
        }
    }

    // Briefly highlights the button for a Command.* value.
    function flash(zone) {
        _flashZone = zone;
        _flashUntil = System.getTimer() + VIEW_FLASH_MS;
        WatchUi.requestUpdate();
    }

    // Shows a short message in the status strip.
    function hint(text) {
        _hintText = text;
        _hintUntil = System.getTimer() + VIEW_HINT_MS;
        WatchUi.requestUpdate();
    }

    function onUpdate(dc) {
        var w = dc.getWidth();
        var h = dc.getHeight();
        var headerH = h * Layout.HEADER_PCT / 100;
        var mainBottom = h * Layout.MAIN_PCT / 100;

        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        drawHeader(dc, w, headerH);

        var rowH = mainBottom - headerH - VIEW_GAP;
        var halfW = w / 2 - VIEW_GAP * 2;
        drawNav(dc, VIEW_GAP, headerH, halfW, rowH, false, "PREV", 0x2B2B2B, Command.PREV);
        drawNav(dc, w / 2 + VIEW_GAP, headerH, halfW, rowH, true, "NEXT", 0x1F6FEB, Command.NEXT);

        var third = w / 3;
        var bottomH = h - mainBottom - VIEW_GAP;
        var smallW = third - VIEW_GAP * 2;
        drawSmall(dc, VIEW_GAP, mainBottom, smallW, bottomH, "START", 0x1E7B34, Command.START);
        drawSmall(dc, third + VIEW_GAP, mainBottom, smallW, bottomH, "BLACK", 0x2B2B2B, Command.BLACK);
        drawSmall(dc, third * 2 + VIEW_GAP, mainBottom, smallW, bottomH, "END", 0x9B2226, Command.END);
    }

    private function drawHeader(dc, w, headerH) {
        var app = getApp();
        var ble = app.ble;
        var linked = (ble != null) && ble.connected;
        var noPin = !app.auth.hasPin();
        var rejected = (ble != null) && ble.sendFailed;

        // What to say, what colour the dot is, and what the right-hand text is.
        var text = "Starting...";
        var dotColor = Graphics.COLOR_ORANGE;
        if (ble != null) {
            text = ble.status;
            if (ble.state == LinkState.CONNECTED) {
                dotColor = Graphics.COLOR_GREEN;
            } else if (ble.state == LinkState.ERROR) {
                dotColor = Graphics.COLOR_RED;
            }
        }

        var right;
        if (noPin) {
            text = "No PIN set";
            dotColor = Graphics.COLOR_RED;
            right = "Set PIN";
        } else if (rejected) {
            dotColor = Graphics.COLOR_RED;
            right = "Set PIN";
        } else if (!linked) {
            right = "Retry";
        } else {
            right = elapsedText();
            if (right == null) {
                right = "Timer";
            }
        }

        var cy = headerH / 2;
        dc.setColor(dotColor, Graphics.COLOR_TRANSPARENT);
        dc.fillCircle(20, cy, 7);

        // A hint temporarily replaces everything else.
        if (_hintText != null) {
            dc.setColor(Graphics.COLOR_YELLOW, Graphics.COLOR_TRANSPARENT);
            TextFit.draw(dc, 36, cy, _hintText, [Graphics.FONT_XTINY], w - 36 - 8,
                         Graphics.TEXT_JUSTIFY_LEFT | Graphics.TEXT_JUSTIFY_VCENTER);
            return;
        }

        // The right-hand text keeps its size; the status gets whatever room is left.
        var rightW = dc.getTextWidthInPixels(right, Graphics.FONT_XTINY);
        var leftMax = w - 36 - rightW - 20;

        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
        TextFit.draw(dc, 36, cy, text, [Graphics.FONT_TINY, Graphics.FONT_XTINY], leftMax,
                     Graphics.TEXT_JUSTIFY_LEFT | Graphics.TEXT_JUSTIFY_VCENTER);

        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
        dc.drawText(w - 12, cy, Graphics.FONT_XTINY, right,
                    Graphics.TEXT_JUSTIFY_RIGHT | Graphics.TEXT_JUSTIFY_VCENTER);
    }

    private function elapsedText() {
        var start = getApp().sessionStart;
        if (start == null) {
            return null;
        }
        var secs = (System.getTimer() - start) / 1000;
        var hrs = secs / 3600;
        var mins = (secs % 3600) / 60;
        var rem = secs % 60;
        if (hrs > 0) {
            return hrs.format("%d") + ":" + mins.format("%02d") + ":" + rem.format("%02d");
        }
        return mins.format("%02d") + ":" + rem.format("%02d");
    }

    private function drawNav(dc, x, y, bw, bh, pointRight, label, color, zone) {
        var active = (_flashZone == zone);
        dc.setColor(active ? Graphics.COLOR_WHITE : color, Graphics.COLOR_BLACK);
        dc.fillRoundedRectangle(x, y, bw, bh, 14);

        dc.setColor(active ? Graphics.COLOR_BLACK : Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
        var cx = x + bw / 2;
        var cy = y + bh / 2 - 14;
        var s = 26;
        var pts;
        if (pointRight) {
            pts = [[cx - s, cy - s], [cx - s, cy + s], [cx + s, cy]];
        } else {
            pts = [[cx + s, cy - s], [cx + s, cy + s], [cx - s, cy]];
        }
        dc.fillPolygon(pts);
        TextFit.draw(dc, cx, cy + s + 24, label, [Graphics.FONT_SMALL, Graphics.FONT_XTINY], bw - 12,
                     Graphics.TEXT_JUSTIFY_CENTER | Graphics.TEXT_JUSTIFY_VCENTER);
    }

    private function drawSmall(dc, x, y, bw, bh, label, color, zone) {
        var active = (_flashZone == zone);
        dc.setColor(active ? Graphics.COLOR_WHITE : color, Graphics.COLOR_BLACK);
        dc.fillRoundedRectangle(x, y, bw, bh, 12);
        dc.setColor(active ? Graphics.COLOR_BLACK : Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
        TextFit.draw(dc, x + bw / 2, y + bh / 2, label, [Graphics.FONT_XTINY], bw - 8,
                     Graphics.TEXT_JUSTIFY_CENTER | Graphics.TEXT_JUSTIFY_VCENTER);
    }
}
