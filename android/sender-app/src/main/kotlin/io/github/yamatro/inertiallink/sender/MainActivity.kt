package io.github.yamatro.inertiallink.sender

import android.app.Activity
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
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

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // The screen contains a live pairing secret; exclude it from screenshots and recent-app previews.
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        setContentView(buildContentView())
        showKey()
        showStoppedState(getString(R.string.status_stopped))
    }

    override fun onStop() {
        stopSender()
        super.onStop()
    }

    override fun onDestroy() {
        sender?.close()
        sender = null
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
            config = UdpSenderConfig(endpoint),
            listener = this,
        )
        sender = newSender
        sessionUsedDisplayedKey = true
        pendingStopError = null
        try {
            newSender.start()
        } catch (error: Exception) {
            newSender.close()
            sender = null
            finishSessionAndRotateKey(error.message ?: getString(R.string.error_start_failed))
        }
    }

    private fun stopSender() {
        val active = sender ?: return
        sender = null
        active.close()
        finishSessionAndRotateKey(null)
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
}
