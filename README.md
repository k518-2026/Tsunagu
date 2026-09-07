# ケーブル診断 Tsunagu

実行ファイルのダウンロード. 
https://github.com/k518-2026/Tsunagu/releases

USB ケーブルの状態を Windows 上から調べるデスクトップアプリです。WPF（C#／.NET 8）で作ってあり、外部ライブラリは使っていません。`Tsunagu.sln` を Visual Studio 2022 で開き、F5 で実行できます。

## 対応言語

日本語・English・简体中文・한국어・Español の 5 言語に対応しています。初回起動時は Windows の表示言語に合わせて選ばれ、右上のドロップダウンでいつでも切り替えられます。選択は `%APPDATA%\Tsunagu\language.txt` に残るので、次回からはその言語で開きます。

言語を足すときは、`Tsunagu/Localization/Strings/en.lang` を複製して言語コードの名前（たとえば `fr.lang`）に変え、右辺を訳してください。プロジェクトに追加すれば実行ファイルに埋め込まれます。ビルドし直さずに足したい場合は、実行ファイルの隣に `lang` フォルダーを作ってそこに `.lang` を置くだけでドロップダウンに現れます。

言語ファイルは `キー = 文言` の 1 行 1 項目です。`{0}` は差し込み位置なので順番を変えないでください。`\n` が改行、行頭の `#` は注釈です。訳が抜けている項目は英語で表示されます。`_name` はドロップダウンに出す言語名、`uiFont` はその言語で使うフォントです。

## ビルド

- Visual Studio 2022（17.8 以降）＋「.NET デスクトップ開発」ワークロード
- `Tsunagu.sln` を開いて F5
- コマンドラインなら `dotnet build Tsunagu.sln -c Release`
- NuGet パッケージの復元は不要です

管理者権限は要りません。

## できること

**接続診断**
USB ホストコントローラーからルートハブ、各ポートへとたどり、ポートごとに「機器が対応している速度」と「実際にリンクしている速度」を取り出して比較します。USB 3.x 対応の機器が USB 2.0 の速度でしかつながっていなければ、ケーブルの高速用の線が使われていないか断線している、という判定が出ます。過電流、電力不足、認識失敗といったハブ側のエラーもここに出ます。

**転送速度**
USB ストレージに実データを書いて読み戻し、実効速度を測ります。`FILE_FLAG_NO_BUFFERING` を使って OS のキャッシュを迂回しているので、キャッシュに騙された速い値は出ません。測定用の一時ファイルは終了時に消します。

**接続の安定性**
`WM_DEVICECHANGE` を受け取るたびに USB ツリーを取り直し、消えた機器と現れた機器を数えます。断線しかけたケーブルは速度ではなく「途切れ」に出るので、監視しながらケーブルの根元を軽く曲げるのがいちばん効きます。

**給電**
バッテリードライバーから充電レート（mW）を直接読み、いま何ワット流れているかを表示します。充電専用の細いケーブルは 5〜7 W 程度で頭打ちになるため、PD 対応ケーブルとの差がそのまま数字に出ます。バッテリーのないデスクトップ機では測定できません。

**レポート保存**
4 つの結果をまとめてテキストファイルに書き出せます。選択中の言語で出力します。

## わかることの限界

ソフトウェアから見えるのは、機器をつないだ状態での通信の結果だけです。ケーブル単体の導体抵抗、E-Marker チップの内容、シールドの質は測れません。ケーブルを単体で検査したい場合は、専用のケーブルテスターが必要です。

同じ機器・同じポートのままケーブルだけを差し替えて比較すると、原因がケーブルか機器かポートかを切り分けられます。

## ファイル構成

```
Tsunagu.sln
Tsunagu/
  Tsunagu.csproj
  app.manifest
  App.xaml / App.xaml.cs           配色・タイポグラフィなどの共通リソース
  MainWindow.xaml / .xaml.cs       画面とロジック
  Native/Win32.cs                  SetupAPI・USB IOCTL・ストレージ・バッテリーの宣言
  Native/DeviceInterfaceEnumerator.cs
  Usb/UsbNode.cs                   ツリーのノードと判定レベル
  Usb/UsbTreeScanner.cs            ハブを走査して判定を組み立てる中核
  Storage/DriveScanner.cs          ドライブ一覧とバス種別（USB か否か）
  Storage/TransferBenchmark.cs     実効転送速度の測定
  Power/BatteryReader.cs           充電ワット数の取得と判定
  Monitor/StabilityMonitor.cs      切断・再接続の監視
  Ui/EmptyToCollapsedConverter.cs
  Localization/Loc.cs              言語ファイルの読み込みと切り替え
  Localization/TExtension.cs       XAML の {loc:T キー} 拡張
  Localization/Strings/*.lang      各言語の文言
```
