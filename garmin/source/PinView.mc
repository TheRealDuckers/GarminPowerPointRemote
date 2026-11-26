using Toybox.Graphics;
using Toybox.Lang;
using Toybox.WatchUi;

// Number pad for typing the PIN shown in the Windows app.
class PinView extends WatchUi.View {
    var digits = "";
    private var _title;

    function initialize(title) {
        View.initialize();
        _title = title;
    }

    function onUpdate(dc) {
        var w = dc.getWidth();
        var h = dc.getHeight();
        var headerH = h * PinLayout.HEADER_PCT / 100;
        var n = digits.length();

        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        // Title.
        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
        TextFit.draw(dc, w / 2, headerH * 28 / 100, _title, [Graphics.FONT_XTINY], w - 16,
                     Graphics.TEXT_JUSTIFY_CENTER | Graphics.TEXT_JUSTIFY_VCENTER);

        // One dot per digit, with empty outlines up to the minimum length.
        var slots = (n < PIN_MIN_LEN) ? PIN_MIN_LEN : n;
        var gap = 26;
        var x0 = w / 2 - ((slots - 1) * gap) / 2;
        var dotY = headerH * 72 / 100;
        for (var i = 0; i < slots; i += 1) {
            if (i < n) {
                dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
                dc.fillCircle(x0 + i * gap, dotY, 8);
            } else {
                dc.setColor(0x555555, Graphics.COLOR_TRANSPARENT);
                dc.drawCircle(x0 + i * gap, dotY, 8);
            }
        }

        // Keys: 1 2 3 / 4 5 6 / 7 8 9 / DEL 0 OK
        var keyW = w / 3;
        var keyH = (h - headerH) / 4;
        for (var idx = 0; idx < 12; idx += 1) {
            var kx = (idx % 3) * keyW + 3;
            var ky = headerH + (idx / 3) * keyH + 3;
            var bw = keyW - 6;
            var bh = keyH - 6;

            var label;
            var color = 0x2B2B2B;
            var textColor = Graphics.COLOR_WHITE;
            if (idx < 9) {
                label = (idx + 1).toString();
            } else if (idx == 9) {
                label = "DEL";
            } else if (idx == 10) {
                label = "0";
            } else {
                label = "OK";
                if (n >= PIN_MIN_LEN) {
                    color = 0x1E7B34;
                } else {
                    color = 0x1A1A1A;
                    textColor = 0x666666;
                }
            }

            dc.setColor(color, Graphics.COLOR_BLACK);
            dc.fillRoundedRectangle(kx, ky, bw, bh, 10);
            dc.setColor(textColor, Graphics.COLOR_TRANSPARENT);
            TextFit.draw(dc, kx + bw / 2, ky + bh / 2, label,
                         [Graphics.FONT_MEDIUM, Graphics.FONT_SMALL, Graphics.FONT_XTINY], bw - 8,
                         Graphics.TEXT_JUSTIFY_CENTER | Graphics.TEXT_JUSTIFY_VCENTER);
        }
    }
}
