# Android送信アプリの準備

参照実装はAndroid API 26以降、compile/target SDK 35、JDK 17を対象とします。`android/`をAndroid Studioで開くか、次を実行します。

```sh
cd android
./gradlew check assembleDebug assembleRelease
```

Windowsでは`gradlew.bat`を使用します。`assembleDebug`は管理された机上試験用のデバッグ署名APKを作り、`assembleRelease`は署名なしの検証用ビルドを作ります。デバッグAPKを配布してはいけません。正式署名、鍵管理、ストア配布は、別途レビューされた保守手順ができるまで対象外です。APK/AABなどの生成物はリポジトリやResearch Previewのリリースへ登録しません。

構成は、通信仕様の`:protocol-kotlin`、センサー・取付変換・較正・送信の`:motion-source-android`、最小UIの`:sender-app`です。カメラ、位置情報、マイク、広告ID、アカウント、広範囲ストレージ権限を追加してはいけません。リリース前に結合後のManifestを確認してください。

v0.1参照アプリはSDK 35を対象とするため、ネットワーク権限は`INTERNET`だけが正しい状態です。将来SDK 37以降（Android 17）を対象にするAndroidホストアプリは、Androidの[ローカルネットワーク権限ガイド](https://developer.android.com/privacy-and-security/local-network-permission)に従い、LAN UDPの前に`ACCESS_LOCAL_NETWORK`を宣言・実行時要求し、拒否・取消時は送受信を停止して中立へ戻してください。SDK 36以下では、この権限を先行追加・要求してはいけません。

接続時は、Unity側のプライベートIPとUDP `28461`を明示入力し、送信セッションごと（開始失敗後やバックグラウンド復帰後を含む）に生成される32桁のペアリングコードをUnityの実行時UIへ入力します。鍵をログ・Scene・スクリーンショット・録画へ残さないでください。画面を上、端末上端を車両前方へ固定し、完全な停車中に静止較正を行います。

ライブラリを組み込む場合、`AuthenticatedUdpMotionSender.close()`は内部に複製した鍵を消去しますが、呼出側が所有する元の`PairingKey`も`finally`で`destroy()`または`close()`する必要があります。Senderは1回開始・1セッション専用です。終了、開始失敗、バックグラウンド移行後は再利用せず、新しい鍵と新しいSenderを作成してください。

最初の実車試験前に[英語版の詳細](android-setup.md)、[較正](calibration.ja.md)、[安全](safety.ja.md)を確認してください。
