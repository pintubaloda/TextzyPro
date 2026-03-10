package com.textzy.mobile

import android.annotation.SuppressLint
import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.view.ViewGroup
import android.webkit.CookieManager
import android.webkit.GeolocationPermissions
import android.webkit.JavascriptInterface
import android.webkit.PermissionRequest
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import org.json.JSONObject
import java.util.UUID

class MainActivity : AppCompatActivity() {

    private lateinit var webView: WebView
    private val baseHost = "textzy-frontend-production.up.railway.app"
    private val baseScheme = "https"
    private val mobileShellUrl = "https://textzy-frontend-production.up.railway.app/?mobileShell=1#mobile-shell"
    private var pendingPermissionRequest: PermissionRequest? = null
    private var pendingGeoOrigin: String? = null
    private var pendingGeoCallback: GeolocationPermissions.Callback? = null
    private val installPrefs by lazy { getSharedPreferences("textzy_mobile_security", MODE_PRIVATE) }
    private val installId by lazy { getOrCreateInstallId() }
    private val cameraPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            val request = pendingPermissionRequest
            if (request != null) {
                if (granted) {
                    request.grant(request.resources)
                } else {
                    request.deny()
                }
            }
            pendingPermissionRequest = null
        }
    private val locationPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { grants ->
            val granted = (grants[Manifest.permission.ACCESS_FINE_LOCATION] == true) ||
                (grants[Manifest.permission.ACCESS_COARSE_LOCATION] == true)
            val origin = pendingGeoOrigin
            val callback = pendingGeoCallback
            if (origin != null && callback != null) {
                callback.invoke(origin, granted, false)
            }
            pendingGeoOrigin = null
            pendingGeoCallback = null
        }

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        webView = WebView(this)
        webView.layoutParams = ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        )

        val settings = webView.settings
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        settings.databaseEnabled = true
        settings.cacheMode = WebSettings.LOAD_DEFAULT
        settings.mediaPlaybackRequiresUserGesture = false
        settings.loadsImagesAutomatically = true
        settings.useWideViewPort = true
        settings.loadWithOverviewMode = true
        settings.mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
        settings.allowFileAccess = false
        settings.allowContentAccess = false
        settings.setGeolocationEnabled(true)
        settings.userAgentString = settings.userAgentString + " TextzyMobileShell/1"

        val cookieManager = CookieManager.getInstance()
        cookieManager.setAcceptCookie(true)
        cookieManager.setAcceptThirdPartyCookies(webView, true)
        webView.addJavascriptInterface(TextzyNativeBridge(), "TextzyNative")

        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView?, request: WebResourceRequest?): Boolean {
                val target = request?.url?.toString().orEmpty()
                val forced = forceMobileShellIfNeeded(target)
                if (forced != null) {
                    view?.loadUrl(forced)
                    return true
                }
                return false
            }

            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                val forced = forceMobileShellIfNeeded(url.orEmpty())
                if (forced != null) {
                    view?.loadUrl(forced)
                }
            }
        }

        webView.webChromeClient = object : WebChromeClient() {
            override fun onPermissionRequest(request: PermissionRequest) {
                runOnUiThread {
                    if (!isTrustedUri(request.origin)) {
                        request.deny()
                        return@runOnUiThread
                    }
                    val wantsCamera = request.resources.contains(PermissionRequest.RESOURCE_VIDEO_CAPTURE)
                    if (!wantsCamera) {
                        request.grant(request.resources)
                        return@runOnUiThread
                    }
                    val granted = ContextCompat.checkSelfPermission(
                        this@MainActivity,
                        Manifest.permission.CAMERA
                    ) == PackageManager.PERMISSION_GRANTED
                    if (granted) {
                        request.grant(request.resources)
                    } else {
                        pendingPermissionRequest?.deny()
                        pendingPermissionRequest = request
                        cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
                    }
                }
            }

            override fun onPermissionRequestCanceled(request: PermissionRequest) {
                if (pendingPermissionRequest == request) {
                    pendingPermissionRequest = null
                }
                super.onPermissionRequestCanceled(request)
            }

            override fun onGeolocationPermissionsShowPrompt(
                origin: String?,
                callback: GeolocationPermissions.Callback?
            ) {
                runOnUiThread {
                    val originUri = runCatching { Uri.parse(origin ?: "") }.getOrNull()
                    if (!isTrustedUri(originUri)) {
                        callback?.invoke(origin, false, false)
                        return@runOnUiThread
                    }
                    val hasLocationPermission =
                        ContextCompat.checkSelfPermission(
                            this@MainActivity,
                            Manifest.permission.ACCESS_FINE_LOCATION
                        ) == PackageManager.PERMISSION_GRANTED ||
                        ContextCompat.checkSelfPermission(
                            this@MainActivity,
                            Manifest.permission.ACCESS_COARSE_LOCATION
                        ) == PackageManager.PERMISSION_GRANTED

                    if (hasLocationPermission) {
                        callback?.invoke(origin, true, false)
                    } else {
                        pendingGeoOrigin = origin
                        pendingGeoCallback = callback
                        locationPermissionLauncher.launch(
                            arrayOf(
                                Manifest.permission.ACCESS_FINE_LOCATION,
                                Manifest.permission.ACCESS_COARSE_LOCATION
                            )
                        )
                    }
                }
            }
        }

        setContentView(webView)

        webView.loadUrl(mobileShellUrl)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (::webView.isInitialized && webView.canGoBack()) {
            webView.goBack()
        } else {
            super.onBackPressed()
        }
    }

    override fun onDestroy() {
        if (::webView.isInitialized) {
            webView.destroy()
        }
        super.onDestroy()
    }

    private fun forceMobileShellIfNeeded(url: String): String? {
        if (url.isBlank()) return null
        val uri = runCatching { Uri.parse(url) }.getOrNull() ?: return null
        if (!isTrustedUri(uri)) return mobileShellUrl

        val hasFlag = uri.getQueryParameter("mobileShell") == "1" || url.contains("mobileShell=1")
        val hasHash = (uri.fragment ?: "").contains("mobile-shell", true) ||
            (uri.fragment ?: "").contains("mobileshell", true)
        if (hasFlag || hasHash) return null
        return mobileShellUrl
    }

    private fun isTrustedUri(uri: Uri?): Boolean {
        if (uri == null) return false
        val scheme = (uri.scheme ?: "").lowercase()
        val host = (uri.host ?: "").lowercase()
        return scheme == baseScheme && host == baseHost
    }

    private fun getOrCreateInstallId(): String {
        val existing = installPrefs.getString("install_id", null)
        if (!existing.isNullOrBlank()) return existing
        val generated = "android-${UUID.randomUUID()}"
        installPrefs.edit().putString("install_id", generated).apply()
        return generated
    }

    private inner class TextzyNativeBridge {
        @JavascriptInterface
        fun getDeviceInfo(): String {
            val appVersion = runCatching {
                packageManager.getPackageInfo(packageName, 0).versionName ?: "1.0.0"
            }.getOrDefault("1.0.0")
            val deviceModel = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
            val payload = JSONObject()
            payload.put("installId", installId)
            payload.put("deviceName", if (deviceModel.isBlank()) "Android Device" else deviceModel)
            payload.put("devicePlatform", "android")
            payload.put("deviceModel", if (deviceModel.isBlank()) "android" else deviceModel)
            payload.put("osVersion", "Android ${Build.VERSION.RELEASE ?: "unknown"}")
            payload.put("appVersion", appVersion)
            return payload.toString()
        }
    }
}
