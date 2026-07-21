package io.github.yamatro.inertiallink.sender

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.text.InputType
import android.view.View
import android.view.WindowManager
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import io.github.yamatro.inertiallink.motion.AndroidMotionSource
import io.github.yamatro.inertiallink.motion.AuthenticatedUdpMotionSender
import io.github.yamatro.inertiallink.motion.CalibrationUpdate
import io.github.yamatro.inertiallink.motion.SenderListener
import io.github.yamatro.inertiallink.motion.SenderStats
import io.github.yamatro.inertiallink.motion.UdpEndpoint
import io.github.yamatro.inertiallink.motion.UdpSenderConfig
import io.github.yamatro.inertiallink.protocol.PairingKey
import java.util.Locale

/** Minimal foreground-only sender UI. Leaving the activity stops all sensor and network activity. */
public class MainActivity : Activity(), SenderListener {
    private lateinit var receiverAddress: EditText
    private lateinit var receiverPort: EditText
    private lateinit var pairingKeyView: TextView
    private lateinit var regenerateButton: Button
    private lateinit var startButton: Button
    private lateinit var stopButton: Button
    private lateinit var calibrateButton: Button
    private lateinit var statusView: TextView

    private var pairingKey: PairingKey = PairingKey.generate()
    private var sender: AuthenticatedUdpMotionSender? = null
    private var sessionUsedDisplayedKey: Boolean = false
    private var pendingStopError: String? = null
    private var debugLocalPort: Int = 0
    private var debugAutomationSession: Boolean = false
    private var debugWakeLock: PowerManager.WakeLock? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        cleanupDebugArtifacts()
        if (BuildConfig.DEBUG && intent?.getBooleanExtra(DEBUG_WAKE_FOR_TEST, false) == true) {
            enableDebugLockedDeviceTest()
        }
        // The screen contains a live pairing secret; exclude it from screenshots and recent-app previews.
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        setContentView(buildContentView())
        showKey()
        showStoppedState(getString(R.string.status_stopped))
        applyDebugAutomation(intent)
    }

    override fun onNewIntent(intent: Intent?) {
        super.onNewIntent(intent)
        setIntent(intent)
        applyDebugAutomation(intent)
    }

    override fun onStop() {
        if (!BuildConfig.DEBUG || !debugAutomationSession) stopSender()
        super.onStop()
    }

    override fun onDestroy() {
        sender?.close()
        sender = null
        releaseDebugWakeLock()
        pairingKey.destroy()
        super.onDestroy()
    }

    override fun onSenderStarted(sessionId: Long) {
        onUi {
            statusView.text = getString(
                R.string.status_sending,
                "%016X".format(Locale.ROOT, sessionId),
            )
        }
    }

    override fun onSenderStats(stats: SenderStats) {
        onUi {
            statusView.text = getString(
                R.string.status_stats,
                stats.imuPacketsSent,
                stats.staleOrBackpressuredFramesDropped,
                stats.syncResponsesSent,
            )
        }
    }

    override fun onCalibrationUpdate(update: CalibrationUpdate) {
        onUi {
            statusView.text = when (update) {
                is CalibrationUpdate.Collecting ->
                    getString(R.string.status_calibrating, update.acceptedSamples, update.requiredSamples)
                is CalibrationUpdate.Completed ->
                    getString(R.string.status_calibrated, update.calibrationId)
                is CalibrationUpdate.Failed ->
                    getString(R.string.status_calibration_failed, update.reason)
            }
        }
    }

    override fun onSenderStopped() {
        onUi {
            val inactive = sender
            sender = null
            inactive?.close()
            releaseDebugWakeLock()
            finishSessionAndRotateKey(pendingStopError)
            pendingStopError = null
        }
    }

    override fun onSenderError(message: String, cause: Throwable?) {
        // Do not log the endpoint, key, packet bytes, or sensor values.
        onUi { pendingStopError = message }
    }

    private fun startSender() {
        if (sender != null) return
        val portText = receiverPort.text.toString()
        val port = if (portText.isNotEmpty() && portText.all { it in '0'..'9' }) portText.toIntOrNull() else null
        if (port == null) {
            receiverPort.error = getString(R.string.error_invalid_port)
            return
        }
        val endpoint = try {
            UdpEndpoint.parse(receiverAddress.text.toString(), port)
        } catch (error: IllegalArgumentException) {
            receiverAddress.error = error.message ?: getString(R.string.error_invalid_receiver)
            return
        }
        setRunningControls(true)
        statusView.text = getString(R.string.status_starting)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        val newSender = AuthenticatedUdpMotionSender(
            motionSource = AndroidMotionSource(applicationContext),
            key = pairingKey,
            config = UdpSenderConfig(endpoint, localPort = if (BuildConfig.DEBUG) debugLocalPort else 0),
            listener = this,
        )
        sender = newSender
        acquireDebugWakeLock()
        sessionUsedDisplayedKey = true
        pendingStopError = null
        try {
            newSender.start()
        } catch (error: Exception) {
            newSender.close()
            sender = null
            releaseDebugWakeLock()
            finishSessionAndRotateKey(error.message ?: getString(R.string.error_start_failed))
        }
    }

    /**
     * ADB UI injection is disabled on some OEM devices. Debug builds therefore
     * accept non-secret test controls through the already-exported launcher
     * activity. Release builds always ignore these extras. A random token may
     * request a short-lived app-private key file for local ADB validation; the
     * file is auto-deleted and stale debug artifacts are removed on next launch.
     */
    private fun applyDebugAutomation(automationIntent: Intent?) {
        if (!BuildConfig.DEBUG || automationIntent == null) return
        if (automationIntent.getBooleanExtra(DEBUG_WAKE_FOR_TEST, false)) {
            enableDebugLockedDeviceTest()
        }
        automationIntent.getStringExtra(DEBUG_RECEIVER_ADDRESS)?.let { receiverAddress.setText(it) }
        automationIntent.getIntExtra(DEBUG_RECEIVER_PORT, -1)
            .takeIf { it in 1024..65535 }
            ?.let { receiverPort.setText(String.format(Locale.ROOT, "%d", it)) }
        automationIntent.getIntExtra(DEBUG_LOCAL_PORT, -1)
            .takeIf { it in 1024..65535 }
            ?.let { debugLocalPort = it }
        automationIntent.getStringExtra(DEBUG_EXPORT_TOKEN)
            ?.lowercase(Locale.ROOT)
            ?.takeIf { DEBUG_TOKEN_PATTERN.matches(it) }
            ?.let { token ->
                val keyFile = cacheDir.resolve("inertiallink-debug-key-$token")
                keyFile.outputStream().bufferedWriter(Charsets.US_ASCII).use {
                    it.write(pairingKey.toDisplayString())
                }
                pairingKeyView.postDelayed({ keyFile.delete() }, DEBUG_KEY_FILE_LIFETIME_MILLIS)
                pairingKeyView.postDelayed({
                    val statusFile = cacheDir.resolve("inertiallink-debug-status-$token")
                    val safeStatus = statusView.text.toString().replace("\n", " ").take(160)
                    statusFile.writeText(
                        "senderActive=${sender != null};addressError=${receiverAddress.error != null};" +
                            "portError=${receiverPort.error != null};status=$safeStatus",
                        Charsets.UTF_8,
                    )
                    pairingKeyView.postDelayed({ statusFile.delete() }, DEBUG_KEY_FILE_LIFETIME_MILLIS)
                }, DEBUG_STATUS_DELAY_MILLIS)
            }
        if (automationIntent.getBooleanExtra(DEBUG_START, false)) startSender()
        if (automationIntent.getBooleanExtra(DEBUG_CALIBRATE, false)) {
            runCatching { sender?.requestStationaryCalibration() }
                .onFailure { statusView.text = getString(R.string.status_calibration_not_started) }
        }
        automationIntent.removeExtra(DEBUG_RECEIVER_ADDRESS)
        automationIntent.removeExtra(DEBUG_RECEIVER_PORT)
        automationIntent.removeExtra(DEBUG_LOCAL_PORT)
        automationIntent.removeExtra(DEBUG_START)
        automationIntent.removeExtra(DEBUG_CALIBRATE)
        automationIntent.removeExtra(DEBUG_WAKE_FOR_TEST)
        automationIntent.removeExtra(DEBUG_EXPORT_TOKEN)
    }

    private fun stopSender() {
        val active = sender ?: return
        sender = null
        active.close()
        releaseDebugWakeLock()
        finishSessionAndRotateKey(null)
    }

    private fun acquireDebugWakeLock() {
        if (!BuildConfig.DEBUG || !debugAutomationSession || debugWakeLock?.isHeld == true) return
        val power = getSystemService(POWER_SERVICE) as PowerManager
        debugWakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "InertialLink:DebugDeviceTest").apply {
            setReferenceCounted(false)
            acquire(DEBUG_WAKE_LOCK_TIMEOUT_MILLIS)
        }
    }

    /** Keep the API-26 debug validation path functional without raising the release minimum SDK. */
    private fun enableDebugLockedDeviceTest() {
        debugAutomationSession = true
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true)
            setTurnScreenOn(true)
        } else {
            @Suppress("DEPRECATION")
            window.addFlags(
                WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or
                    WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON,
            )
        }
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }

    private fun releaseDebugWakeLock() {
        debugWakeLock?.takeIf { it.isHeld }?.release()
        debugWakeLock = null
    }

    private fun cleanupDebugArtifacts() {
        if (!BuildConfig.DEBUG) return
        cacheDir.listFiles()
            ?.filter { it.isFile && it.name.startsWith("inertiallink-debug-") }
            ?.forEach { it.delete() }
    }

    private fun regeneratePairingKey() {
        check(sender == null) { "Cannot change key while sending" }
        pairingKey.destroy()
        pairingKey = PairingKey.generate()
        showKey()
        statusView.text = getString(R.string.status_key_regenerated)
    }

    private fun showKey() {
        pairingKeyView.text = pairingKey.toDisplayString()
    }

    /** A sender session and displayed key are one-use together, including failed starts. */
    private fun finishSessionAndRotateKey(error: String?) {
        if (!sessionUsedDisplayedKey) return
        sessionUsedDisplayedKey = false
        pairingKey.destroy()
        pairingKey = PairingKey.generate()
        showKey()
        val message = if (error == null) {
            getString(R.string.status_stopped_repair)
        } else {
            getString(R.string.status_stopped_error_repair, error)
        }
        showStoppedState(message)
    }

    private fun showStoppedState(message: String) {
        window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        setRunningControls(false)
        statusView.text = message
    }

    private fun setRunningControls(isRunning: Boolean) {
        receiverAddress.isEnabled = !isRunning
        receiverPort.isEnabled = !isRunning
        regenerateButton.isEnabled = !isRunning
        startButton.isEnabled = !isRunning
        stopButton.isEnabled = isRunning
        calibrateButton.isEnabled = isRunning
        pairingKeyView.text = if (isRunning) getString(R.string.pairing_key_hidden) else pairingKey.toDisplayString()
        pairingKeyView.contentDescription = getString(
            if (isRunning) R.string.pairing_key_hidden_description else R.string.pairing_key_description,
        )
    }

    private fun buildContentView(): View {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(20), dp(20), dp(32))
            setBackgroundColor(Color.rgb(245, 250, 248))
        }
        content.addView(text(getString(R.string.app_name), 26f, Typeface.BOLD))
        content.addView(text(getString(R.string.intro), 15f).withMargins(top = 8))
        content.addView(text(getString(R.string.safety), 15f, Typeface.BOLD, Color.rgb(130, 45, 20)).withMargins(top = 14))

        content.addView(label(getString(R.string.receiver_ip)).withMargins(top = 22))
        receiverAddress = EditText(this).apply {
            hint = getString(R.string.receiver_hint)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            isSingleLine = true
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
        }
        content.addView(receiverAddress, matchWrap())

        content.addView(label(getString(R.string.receiver_port)).withMargins(top = 12))
        receiverPort = EditText(this).apply {
            setText(getString(R.string.default_port))
            inputType = InputType.TYPE_CLASS_NUMBER
            isSingleLine = true
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
        }
        content.addView(receiverPort, matchWrap())

        content.addView(label(getString(R.string.pairing_key)).withMargins(top = 18))
        pairingKeyView = text("", 17f, Typeface.BOLD).apply {
            typeface = Typeface.MONOSPACE
            setTextIsSelectable(false)
            isLongClickable = false
            contentDescription = getString(R.string.pairing_key_description)
        }
        content.addView(pairingKeyView, matchWrap())
        content.addView(text(getString(R.string.pairing_help), 13f).withMargins(top = 4))

        regenerateButton = Button(this).apply {
            text = getString(R.string.regenerate_key)
            setOnClickListener { regeneratePairingKey() }
        }
        content.addView(regenerateButton, matchWrap().withMargins(top = 10))

        startButton = Button(this).apply {
            text = getString(R.string.start)
            setOnClickListener { startSender() }
        }
        content.addView(startButton, matchWrap().withMargins(top = 18))

        stopButton = Button(this).apply {
            text = getString(R.string.stop)
            setOnClickListener { stopSender() }
        }
        content.addView(stopButton, matchWrap().withMargins(top = 6))

        calibrateButton = Button(this).apply {
            text = getString(R.string.calibrate)
            setOnClickListener {
                runCatching { sender?.requestStationaryCalibration() }
                    .onFailure { statusView.text = getString(R.string.status_calibration_not_started) }
            }
        }
        content.addView(calibrateButton, matchWrap().withMargins(top = 6))

        statusView = text("", 15f, Typeface.BOLD).apply {
            setPadding(dp(12), dp(12), dp(12), dp(12))
            setBackgroundColor(Color.rgb(226, 239, 235))
        }
        content.addView(statusView, matchWrap().withMargins(top = 18))

        return ScrollView(this).apply { addView(content) }
    }

    private fun text(value: String, sizeSp: Float, style: Int = Typeface.NORMAL, color: Int = Color.rgb(25, 35, 32)): TextView =
        TextView(this).apply {
            text = value
            textSize = sizeSp
            setTextColor(color)
            setTypeface(typeface, style)
        }

    private fun label(value: String): TextView = text(value, 14f, Typeface.BOLD)

    private fun View.withMargins(top: Int = 0): View {
        layoutParams = matchWrap().withMargins(top)
        return this
    }

    private fun LinearLayout.LayoutParams.withMargins(top: Int): LinearLayout.LayoutParams = apply {
        topMargin = dp(top)
    }

    private fun matchWrap(): LinearLayout.LayoutParams = LinearLayout.LayoutParams(
        LinearLayout.LayoutParams.MATCH_PARENT,
        LinearLayout.LayoutParams.WRAP_CONTENT,
    )

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private inline fun onUi(crossinline action: () -> Unit) {
        if (isFinishing || isDestroyed) return
        runOnUiThread { if (!isFinishing && !isDestroyed) action() }
    }

    private companion object {
        const val DEBUG_RECEIVER_ADDRESS = "io.github.yamatro.inertiallink.debug.RECEIVER_ADDRESS"
        const val DEBUG_RECEIVER_PORT = "io.github.yamatro.inertiallink.debug.RECEIVER_PORT"
        const val DEBUG_LOCAL_PORT = "io.github.yamatro.inertiallink.debug.LOCAL_PORT"
        const val DEBUG_START = "io.github.yamatro.inertiallink.debug.START"
        const val DEBUG_CALIBRATE = "io.github.yamatro.inertiallink.debug.CALIBRATE"
        const val DEBUG_WAKE_FOR_TEST = "io.github.yamatro.inertiallink.debug.WAKE_FOR_TEST"
        const val DEBUG_EXPORT_TOKEN = "io.github.yamatro.inertiallink.debug.EXPORT_TOKEN"
        const val DEBUG_KEY_FILE_LIFETIME_MILLIS = 30_000L
        const val DEBUG_STATUS_DELAY_MILLIS = 2_000L
        const val DEBUG_WAKE_LOCK_TIMEOUT_MILLIS = 5 * 60 * 1_000L
        val DEBUG_TOKEN_PATTERN: Regex = Regex("[0-9a-f]{32}")
    }
}
