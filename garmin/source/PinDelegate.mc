using Toybox.Attention;
using Toybox.Lang;
using Toybox.System;
using Toybox.WatchUi;

class PinDelegate extends WatchUi.BehaviorDelegate {
    private var _view;
    private var _firstRun;

    function initialize(view, firstRun) {
        BehaviorDelegate.initialize();
        _view = view;
        _firstRun = firstRun;
    }

    function onTap(evt) {
        var coords = evt.getCoordinates();
        var settings = System.getDeviceSettings();
        var key = PinLayout.keyAt(coords[0], coords[1], settings.screenWidth, settings.screenHeight);

        if (key == PinLayout.KEY_NONE) {
            return true;
        }
        if (key == PinLayout.KEY_OK) {
            confirm();
            return true;
        }
        if (key == PinLayout.KEY_DEL) {
            var len = _view.digits.length();
            if (len > 0) {
                _view.digits = _view.digits.substring(0, len - 1);
            }
        } else if (_view.digits.length() < PIN_MAX_LEN) {
            _view.digits = _view.digits + key.toString();
        }
        vibrate(40, 30);
        WatchUi.requestUpdate();
        return true;
    }

    // The physical button confirms, like OK.
    function onKey(evt) {
        if (evt.getKey() == WatchUi.KEY_ENTER) {
            confirm();
            return true;
        }
        return false;
    }

    private function confirm() {
        if (_view.digits.length() < PIN_MIN_LEN) {
            vibrate(100, 200);
            return;
        }
        var app = getApp();
        app.auth.setPin(_view.digits);
        if (app.ble != null) {
            app.ble.pinChanged();
        }
        vibrate(60, 80);
        if (_firstRun) {
            WatchUi.switchToView(app.view, new RemoteInputDelegate(app.view), WatchUi.SLIDE_LEFT);
        } else {
            WatchUi.popView(WatchUi.SLIDE_DOWN);
        }
    }

    private function vibrate(strength, ms) {
        if (Attention has :vibrate) {
            Attention.vibrate([new Attention.VibeProfile(strength, ms)]);
        }
    }
}
