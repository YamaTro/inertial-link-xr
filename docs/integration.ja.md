# Unity/OpenXRへの導入

Unity 2022.3 LTS以降のPackage Managerで「+ → Add package from disk」を選び、次を指定します。

```text
unity/io.github.yamatro.inertiallink.xr/package.json
```

## まず合成データで確認

1. 空のGameObjectへ`SyntheticMotionSource`と`VehicleMotionHub`を追加します。
2. CameraやXR Originを含まず、その配下にもならない、テスト表示専用の`contentRoot`を作ります。
3. `EnvironmentMotionDriver`へHubと`contentRoot`を設定します。
4. ゴーグルを装着せずPlayし、送信元を停止すると中立へ戻ることを確認します。

## Androidと接続

同じGameObjectへ`UdpMotionSource`と`VehicleMotionHub`を追加します。ペアリング鍵はSceneやPrefabへ保存せず、Androidの現在のセッションに表示された32桁の16進コードを実行時に渡します。

```csharp
if (!udp.ConfigurePairingKey(oneSessionCode)) return;
if (!hub.SetSource(udp)) return;
if (!driver.Configure(hub, visualContentRoot)) return;
```

AndroidにはゴーグルのプライベートIPとUDP `28461`を明示入力します。公開Wi-Fiは避けてください。認証済みパケットを受信した後に相手を固定し、時刻同期と3パケットのウォームアップを終えるまで出力しません。

独自のキューは`VehicleMotionHub.Current`または`MotionUpdated`から値を取得し、必ず`SafetyWeight`を乗じ、指定キューだけへ適用します。加速度から無制限な位置を積分せず、Camera/XR Originを操作しないでください。詳しいコードとライフサイクルは[英語版](integration.md)、導入前の確認は[安全](safety.ja.md)と[較正](calibration.ja.md)を参照してください。

Android 17 / SDK 37以降を対象とするホストアプリでは、LAN UDPの前に`ACCESS_LOCAL_NETWORK`の実行時許可が必要です。拒否・取消時はネットワーク喪失として送受信を停止し、中立へ戻してください。SDK 36以下では`INTERNET`による暗黙許可を使い、新権限を先行要求しません。
