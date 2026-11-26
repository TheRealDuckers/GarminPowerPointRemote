using Toybox.Attention;
using Toybox.Lang;
using Toybox.System;
using Toybox.WatchUi;

const BACK_CONFIRM_MS = 2500;

// Handles taps, the physical button, and the Back (swipe right) gesture.
//
// onKey() is used for the button instead of onSelect(): on some touch watches the
// sole button reports KEY_ENTER but never reaches onSelect(). Any other key
// returns false so the default behaviours still work.
class RemoteInputDelegate extends WatchUi.BehaviorDelegate {
    private var _view;
    private var _lastBackAt = null;

    function initialize(view) {
        BehaviorDelegate.initialize();
        _view = view;
    }

    function onTap(evt) {
        var coords = evt.getCoordinates();
        var settings = System.getDeviceSettings();
        var zone = Layout.zoneAt(coords[0], coords[1], settings.screenWidth, settings.screenHeight);

        if (zone == Layout.ZONE_HEADER) {
            onHeaderTap();
        } else {
            press(zone);
        }
        return true;
    }

    // The watch's physical button = next slide.
    function onKey(evt) {
        if (evt.getKey() == WatchUi.KEY_ENTER) {
            press(Command.NEXT);
            return true;
        }
        return false;
    }

    // A stray swipe mid-talk would close the app and drop the connection, so it takes two.
    function onBack() {
        var now = System.getTimer();
        if (_lastBackAt != null && (now - _lastBackAt) < BACK_CONFIRM_MS) {
            return false;  // second swipe: default Back behaviour exits the app
        }
        _lastBackAt = now;
        _view.hint("Swipe again to exit");
        vibrate(50, 40);
        return true;
    }

    // No PIN or PIN rejected: open the number pad. Disconnected: retry the search.
    // Connected: start/stop the talk timer.
    private function onHeaderTap() {
        var app = getApp();
        var ble = app.ble;

        if (!app.auth.hasPin() || (ble != null && ble.sendFailed)) {
            openPinPad();
        } else if (ble != null && ble.connected) {
            app.sessionStart = (app.sessionStart == null) ? System.getTimer() : null;
            WatchUi.requestUpdate();
            vibrate(50, 40);
        } else if (ble != null) {
            ble.rescan();
        }
    }

    private function openPinPad() {
        var pinView = new PinView("Enter PIN from your PC");
        WatchUi.pushView(pinView, new PinDelegate(pinView, false), WatchUi.SLIDE_UP);
    }

    private function press(command) {
        var app = getApp();
        if (app.ble != null && app.ble.send(command)) {
            _view.flash(command);
            vibrate(50, 40);
            if (command == Command.START) {
                app.sessionStart = System.getTimer();
            } else if (command == Command.END) {
                app.sessionStart = null;
            }
        } else {
            vibrate(100, 200);  // long buzz: not connected
        }
    }

    private function vibrate(strength, ms) {
        if (Attention has :vibrate) {
            Attention.vibrate([new Attention.VibeProfile(strength, ms)]);
        }
    }
}
