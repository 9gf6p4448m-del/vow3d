// WebGL haptics backend for HapticFeedbackService.
// navigator.vibrate works in Android browsers; iOS Safari has no Vibration API, so this silently does nothing there.
mergeInto(LibraryManager.library, {
  VowVibrate: function (milliseconds) {
    if (typeof navigator !== 'undefined' && typeof navigator.vibrate === 'function') {
      try { navigator.vibrate(milliseconds); } catch (e) { /* blocked until the first user gesture; ignore */ }
    }
  }
});
