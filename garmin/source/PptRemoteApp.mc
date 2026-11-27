using Toybox.Application;
using Toybox.Lang;
using Toybox.System;
using Toybox.Timer;
using Toybox.WatchUi;

const TICK_MS = 250;

class PptRemoteApp extends Application.AppBase {
    var ble = null;
    var auth = null;
    var view = null;
    var sessionStart = null;  // System.getTimer() value when the talk timer started, or null
    private var _ticker = null;

    function initialize() {
        AppBase.initialize();
    }

    function onStart(state) {
        auth = new PinAuth();
        ble = new RemoteBle();
        ble.start();

        // Connect IQ allows only 3 Timer objects per app, so everything shares this one.
        _ticker = new Timer.Timer();
        _ticker.start(method(:onTick), TICK_MS, true);
    }

    function onStop(state) {
        if (_ticker != null) {
            _ticker.stop();
        }
        if (ble != null) {
            ble.stop();
        }
    }

    function onTick() as Void {
        var now = System.getTimer();
        if (ble != null) {
            ble.tick(now);
        }
        if (view != null) {
            view.tick(now);
        }
    }

    function getInitialView() {
        view = new RemoteView();
        if (!auth.hasPin()) {
            // First launch: ask for the PIN before anything else.
            var pinView = new PinView("Enter PIN from your PC");
            return [pinView, new PinDelegate(pinView, true)];
        }
        return [view, new RemoteInputDelegate(view)];
    }
}

function getApp() {
    return Application.getApp() as PptRemoteApp;
}
