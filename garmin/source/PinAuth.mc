using Toybox.Application;
using Toybox.Cryptography;
using Toybox.Lang;
using Toybox.Time;

// Signs every command with the PIN so the PC can ignore anything else.
//
// Packet (13 bytes): [command][counter, 4 bytes big-endian][first 8 bytes of
// HMAC-SHA256(key, command + counter)], where key = SHA-256("PPTRemote-v1:" + PIN).
// The counter only ever goes up, which stops recorded packets being replayed.
class PinAuth {
    private var _pin = null;
    private var _key = null;
    private var _counter = 0;

    function initialize() {
        var stored = Application.Storage.getValue(STORE_PIN);
        if (stored instanceof Lang.String && isValid(stored)) {
            setKey(stored);
        }
        var saved = Application.Storage.getValue(STORE_COUNTER);
        if (saved instanceof Lang.Number) {
            _counter = saved;
        }
    }

    function hasPin() {
        return _key != null;
    }

    function isValid(pin) {
        return pin.length() >= PIN_MIN_LEN && pin.length() <= PIN_MAX_LEN;
    }

    function setPin(pin) {
        Application.Storage.setValue(STORE_PIN, pin);
        setKey(pin);
    }

    // Returns the 13-byte packet for a Command.* value, or null if no PIN is set.
    function sign(command) {
        if (_key == null) {
            return null;
        }

        // Seed from the clock so a reinstall can't wind the counter back.
        var now = Time.now().value();
        _counter = (_counter + 1 > now) ? _counter + 1 : now;
        Application.Storage.setValue(STORE_COUNTER, _counter);

        var msg = new [5]b;
        msg[0] = command;
        msg[1] = (_counter >> 24) & 0xFF;
        msg[2] = (_counter >> 16) & 0xFF;
        msg[3] = (_counter >> 8) & 0xFF;
        msg[4] = _counter & 0xFF;

        var hmac = new Cryptography.HashBasedMessageAuthenticationCode({
            :algorithm => Cryptography.HASH_SHA256,
            :key => _key
        });
        hmac.update(msg);
        var mac = hmac.digest();

        var packet = new [13]b;
        for (var i = 0; i < 5; i += 1) {
            packet[i] = msg[i];
        }
        for (var i = 0; i < 8; i += 1) {
            packet[5 + i] = mac[i];
        }
        return packet;
    }

    private function setKey(pin) {
        try {
            var hash = new Cryptography.Hash({:algorithm => Cryptography.HASH_SHA256});
            hash.update(toBytes(PIN_SALT + pin));
            _key = hash.digest();
            _pin = pin;
        } catch (ex) {
            _key = null;
            _pin = null;
        }
    }

    private function toBytes(text) {
        var chars = text.toUtf8Array();
        var bytes = new [chars.size()]b;
        for (var i = 0; i < chars.size(); i += 1) {
            bytes[i] = chars[i];
        }
        return bytes;
    }
}
