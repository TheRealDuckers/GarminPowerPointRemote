using Toybox.Lang;

// Must match windows/Protocol.cs
const SERVICE_UUID = "7d9b0001-5a4c-4f6e-9a3b-2c8f1e0d4b11";
const COMMAND_CHAR_UUID = "7d9b0002-5a4c-4f6e-9a3b-2c8f1e0d4b11";

// PIN / packet signing. Must match windows/Auth.cs and docs/protocol.md
const PIN_MIN_LEN = 6;
const PIN_MAX_LEN = 12;
const PIN_SALT = "PPTRemote-v1:";
const STORE_PIN = "pin";
const STORE_COUNTER = "counter";

module Command {
    const HELLO = 0x00;  // sent once on connect so the PC can show "watch connected"
    const NEXT = 0x01;
    const PREV = 0x02;
    const START = 0x03;
    const END = 0x04;
    const BLACK = 0x05;
}

module LinkState {
    const SEARCHING = 0;
    const CONNECTING = 1;
    const CONNECTED = 2;
    const ERROR = 3;
}

// Screen layout shared by the view (drawing) and the input delegate (hit testing).
module Layout {
    const HEADER_PCT = 18;  // status strip height, % of screen
    const MAIN_PCT = 72;    // bottom edge of the PREV/NEXT row, % of screen
    const ZONE_HEADER = 0;  // returned by zoneAt() for the status strip

    // Returns a Command.* value, or ZONE_HEADER.
    function zoneAt(x, y, w, h) {
        var headerH = h * HEADER_PCT / 100;
        var mainBottom = h * MAIN_PCT / 100;

        if (y < headerH) {
            return ZONE_HEADER;
        }
        if (y < mainBottom) {
            return (x < w / 2) ? Command.PREV : Command.NEXT;
        }
        var third = w / 3;
        if (x < third) {
            return Command.START;
        }
        if (x < third * 2) {
            return Command.BLACK;
        }
        return Command.END;
    }
}

// Keeps text inside its box: tries each font (largest first), then truncates with "...".
module TextFit {
    // Returns [font, text].
    function fit(dc, text, fonts as Array, maxW) as Array {
        for (var i = 0; i < fonts.size(); i += 1) {
            if (dc.getTextWidthInPixels(text, fonts[i]) <= maxW) {
                return [fonts[i], text];
            }
        }
        var font = fonts[fonts.size() - 1];
        var s = text;
        while (s.length() > 1 && dc.getTextWidthInPixels(s + "...", font) > maxW) {
            s = s.substring(0, s.length() - 1);
        }
        return [font, s + "..."];
    }

    function draw(dc, x, y, text, fonts as Array, maxW, justify) {
        var f = fit(dc, text, fonts, maxW);
        dc.drawText(x, y, f[0], f[1], justify);
    }
}

// Keypad layout shared by PinView (drawing) and PinDelegate (hit testing).
module PinLayout {
    const HEADER_PCT = 22;
    const KEY_NONE = -1;
    const KEY_DEL = 10;
    const KEY_OK = 11;

    // Returns 0-9 for digit keys, KEY_DEL, KEY_OK, or KEY_NONE for the header.
    function keyAt(x, y, w, h) {
        var headerH = h * HEADER_PCT / 100;
        if (y < headerH) {
            return KEY_NONE;
        }
        var col = x * 3 / w;
        var row = (y - headerH) * 4 / (h - headerH);
        if (col > 2) {
            col = 2;
        }
        if (row > 3) {
            row = 3;
        }
        var idx = row * 3 + col;  // keys: 1 2 3 / 4 5 6 / 7 8 9 / DEL 0 OK
        if (idx < 9) {
            return idx + 1;
        }
        if (idx == 9) {
            return KEY_DEL;
        }
        if (idx == 10) {
            return 0;
        }
        return KEY_OK;
    }
}
