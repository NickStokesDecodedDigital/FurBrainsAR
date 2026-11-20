package com.decoded.furbrains_host

import com.learntoflutter.flutter_embed_unity_android.unity.IFakeUnityPlayerActivity
import io.flutter.embedding.android.FlutterFragmentActivity

// Hosts both Flutter and the embedded Unity game.
//
// Why FlutterFragmentActivity, not FlutterActivity (flutter_embed_unity issue #21):
// when AR starts, Unity lists every method of this activity by reflection. FlutterActivity has methods using
// Android 13+ back-gesture classes (OnBackInvokedCallback, BackEvent); on Android 12 and older those classes don't
// exist and the listing throws, so the AR scene never appears. FlutterFragmentActivity doesn't have them (yet: a
// future Flutter update could add them and break this).
//
// IFakeUnityPlayerActivity: Unity's AR code expects to be hosted by Unity's own UnityPlayerActivity and reads its
// field "mUnityPlayer". The plugin fills this field in through setmUnityPlayer. @JvmField makes it a plain public
// Java field (not a Kotlin getter/setter pair), so Unity can find it by name. Don't rename it.
class MainActivity : FlutterFragmentActivity(), IFakeUnityPlayerActivity {
    @JvmField
    var mUnityPlayer: Any? = null

    override fun setmUnityPlayer(mUnityPlayer: Any?) {
        this.mUnityPlayer = mUnityPlayer
    }
}
