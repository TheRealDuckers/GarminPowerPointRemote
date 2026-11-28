using Toybox.Attention;
using Toybox.BluetoothLowEnergy;
using Toybox.Lang;
using Toybox.System;
using Toybox.WatchUi;

const BLE_MAX_QUEUE = 4;
const BLE_CONNECT_TIMEOUT_MS = 12000;
const BLE_RETRY_DELAY_MS = 2000;
const BLE_HEARTBEAT_MS = 10000;
const BLE_WRITE_TIMEOUT_MS = 4000;

// Finds the Windows PC (by service UUID), connects, and writes 1-byte commands.
//
// Connect IQ only allows 3 Timer objects per app, so this class owns none:
// the app's single ticker calls tick() and all delays are deadlines checked there.
class RemoteBle extends BluetoothLowEnergy.BleDelegate {
    private var _serviceUuid;
    private var _charUuid;
    private var _device = null;
    private var _characteristic = null;
    private var _profileReady = false;
    private var _writeBusy = false;
    private var _writeStartedAt = 0;
    private var _queue as Lang.Array<Lang.Number> = [];
    private var _retryAt = null;
    private var _timeoutAt = null;
    private var _nextHeartbeatAt = 0;

    var status = "Starting...";
    var state = LinkState.SEARCHING;
    var connected = false;
    var sendFailed = false;  // true while writes are being rejected (usually a wrong PIN)

    function initialize() {
        BleDelegate.initialize();
        _serviceUuid = BluetoothLowEnergy.stringToUuid(SERVICE_UUID);
        _charUuid = BluetoothLowEnergy.stringToUuid(COMMAND_CHAR_UUID);
    }

    function start() {
        BluetoothLowEnergy.setDelegate(me);
        var profile = {
            :uuid => _serviceUuid,
            :characteristics => [{
                :uuid => _charUuid
            }]
        };
        try {
            BluetoothLowEnergy.registerProfile(profile);
        } catch (ex) {
            setState(LinkState.ERROR, "No BLE");
        }
    }

    function stop() {
        try {
            BluetoothLowEnergy.setScanState(BluetoothLowEnergy.SCAN_STATE_OFF);
            if (_device != null) {
                BluetoothLowEnergy.unpairDevice(_device);
            }
        } catch (ex) {
            // Shutting down anyway.
        }
    }

    // Called by the app's single ticker (every 250 ms).
    function tick(now) {
        if (_retryAt != null && (now - _retryAt) >= 0) {
            _retryAt = null;
            startScan();
        }

        if (_timeoutAt != null && (now - _timeoutAt) >= 0) {
            _timeoutAt = null;
            if (!connected) {
                // The PC never answered: drop the attempt and search again.
                if (_device != null) {
                    unpair(_device);
                    _device = null;
                }
                startScan();
            }
        }

        if (connected) {
            // A write whose callback never arrives would block every later tap.
            if (_writeBusy && (now - _writeStartedAt) >= BLE_WRITE_TIMEOUT_MS) {
                _writeBusy = false;
                _queue = [];
                sendFailed = true;
                status = "Timed out";
                WatchUi.requestUpdate();
            }
            // Heartbeat so the PC can tell the watch is still there.
            if (!_writeBusy && (now - _nextHeartbeatAt) >= 0) {
                _nextHeartbeatAt = now + BLE_HEARTBEAT_MS;
                send(Command.HELLO);
            }
        }
    }

    function onProfileRegister(uuid, result) {
        if (result == BluetoothLowEnergy.STATUS_SUCCESS) {
            _profileReady = true;
            startScan();
        } else {
            setState(LinkState.ERROR, "BLE error");
        }
    }

    function startScan() {
        if (!_profileReady) {
            return;
        }
        _retryAt = null;
        _timeoutAt = null;
        setState(LinkState.SEARCHING, "Searching...");
        try {
            BluetoothLowEnergy.setScanState(BluetoothLowEnergy.SCAN_STATE_SCANNING);
        } catch (ex) {
            setState(LinkState.ERROR, "BT off?");
        }
    }

    // Tapping the status strip while disconnected retries from scratch.
    function rescan() {
        if (connected) {
            return;
        }
        if (_device != null) {
            unpair(_device);
            _device = null;
        }
        startScan();
    }

    function onScanStateChange(scanState, result) {
        if (result != BluetoothLowEnergy.STATUS_SUCCESS) {
            setState(LinkState.ERROR, "Scan failed");
        }
    }

    function onScanResults(scanResults) {
        // Results can still arrive after scanning is switched off; only act while searching.
        if (state != LinkState.SEARCHING) {
            return;
        }
        for (var item = scanResults.next(); item != null; item = scanResults.next()) {
            if (item instanceof BluetoothLowEnergy.ScanResult) {
                if (hasService(item.getServiceUuids(), _serviceUuid)) {
                    connectTo(item);
                    return;
                }
            }
        }
    }

    private function connectTo(scanResult) {
        try {
            BluetoothLowEnergy.setScanState(BluetoothLowEnergy.SCAN_STATE_OFF);
            setState(LinkState.CONNECTING, "Connecting...");
            // pairDevice() throws DevicePairException if pairing fails.
            _device = BluetoothLowEnergy.pairDevice(scanResult);
        } catch (ex) {
            _device = null;
        }

        var now = System.getTimer();
        if (_device == null) {
            setState(LinkState.ERROR, "Pair failed");
            _retryAt = now + BLE_RETRY_DELAY_MS;
        } else {
            _timeoutAt = now + BLE_CONNECT_TIMEOUT_MS;
        }
    }

    function onConnectedStateChanged(device, connectionState) {
        var now = System.getTimer();

        if (connectionState == BluetoothLowEnergy.CONNECTION_STATE_CONNECTED) {
            _timeoutAt = null;
            var service = device.getService(_serviceUuid);
            var ch = (service != null) ? service.getCharacteristic(_charUuid) : null;
            if (ch != null) {
                _device = device;
                _characteristic = ch;
                _writeBusy = false;
                sendFailed = false;
                _queue = [];
                _nextHeartbeatAt = now + BLE_HEARTBEAT_MS;
                setState(LinkState.CONNECTED, "Connected");
                send(Command.HELLO);
            } else {
                setState(LinkState.ERROR, "Wrong device");
                unpair(device);
                _device = null;
                _retryAt = now + BLE_RETRY_DELAY_MS;
            }
        } else {
            var wasConnected = connected;
            unpair(device);
            _device = null;
            _characteristic = null;
            _writeBusy = false;
            _queue = [];
            startScan();
            if (wasConnected) {
                alertLost();
            }
        }
    }

    // Sends a Command.* byte. Returns false if not connected.
    function send(command) {
        if (!connected || _characteristic == null) {
            return false;
        }
        if (_writeBusy) {
            // Only one BLE operation can be in flight; queue a few quick taps.
            if (_queue.size() < BLE_MAX_QUEUE) {
                _queue.add(command);
            }
            return true;
        }
        return write(command);
    }

    function onCharacteristicWrite(characteristic, result) {
        _writeBusy = false;
        if (result == BluetoothLowEnergy.STATUS_SUCCESS) {
            if (sendFailed) {
                sendFailed = false;
                status = "Connected";
                WatchUi.requestUpdate();
            }
        } else {
            sendFailed = true;
            status = "Check PIN";
            WatchUi.requestUpdate();
        }
        if (_queue.size() > 0) {
            var next = _queue[0];
            _queue = _queue.slice(1, null);
            write(next);
        }
    }

    private function write(command) {
        // Every command is signed with the PIN; no PIN means nothing can be sent.
        var data = getApp().auth.sign(command);
        if (data == null) {
            return false;
        }
        try {
            _characteristic.requestWrite(data, {:writeType => BluetoothLowEnergy.WRITE_TYPE_WITH_RESPONSE});
            _writeBusy = true;
            _writeStartedAt = System.getTimer();
            return true;
        } catch (ex) {
            sendFailed = true;
            status = "Send failed";
            WatchUi.requestUpdate();
            return false;
        }
    }

    // Called after a new PIN is entered on the watch.
    function pinChanged() {
        sendFailed = false;
        _queue = [];
        if (connected) {
            status = "Connected";
            WatchUi.requestUpdate();
            send(Command.HELLO);
        }
    }

    private function hasService(iter, uuid) {
        for (var u = iter.next(); u != null; u = iter.next()) {
            if (u.equals(uuid)) {
                return true;
            }
        }
        return false;
    }

    private function unpair(device) {
        try {
            BluetoothLowEnergy.unpairDevice(device);
        } catch (ex) {
            // Already unpaired.
        }
    }

    // Double buzz when a live connection drops, so you notice mid-talk.
    private function alertLost() {
        if (Attention has :vibrate) {
            Attention.vibrate([
                new Attention.VibeProfile(100, 150),
                new Attention.VibeProfile(0, 100),
                new Attention.VibeProfile(100, 150)
            ]);
        }
    }

    private function setState(newState, text) {
        state = newState;
        status = text;
        connected = (newState == LinkState.CONNECTED);
        WatchUi.requestUpdate();
    }
}
