Market Voyager 1.0.1.8 — price-check history and market approach

- リスト設定に「6時間以内に価格確認したワールドを除外」を追加。リストごとにオン・オフ可能（初期オン）。正常な価格取得で更新し、購入なし・出品なしも対象。移動だけ・画面を開くだけ・取得失敗は対象外。
- ワールドごとの最終価格確認と、所持品で確認できた最終購入を別々に記録。6時間経過後は再訪可能。履歴は除外オフでも保持。
- 現在のDCから、DCごとにまとめて巡回。開始位置がリムサのメインエーテライト付近でなければLifestreamでテレポして開始。巡回中のエーテライトへの帰路は徒歩を維持。
- マーケットへの移動中はvnavmeshでカメラを進行方向に合わせ、終了時に元の追従設定へ復元。2m以内のボードへのアクセスを、カメラの視線チェックに依存しない通常オブジェクト操作へ変更。OSの前面化・入力送信は使用しません。
- 価格確認の保存は初回を即時保存し、その後は取引保存・移動前・停止時、または30秒ごとにまとめます。購入のUI経路・価格上限・所持数照合は維持。

Validation: clean Release with zero warnings/errors; 960 core assertions, 548 native checks, 1074 UI/glyph/lifecycle checks and 46 background renders. Seven-language history UI, camera restoration/error paths, teleport IPC, bounded/coalesced history and ordinary interaction dispatch verified offline. Actual in-game navigation, teleport, camera and background-window behavior remain unverified; public preview.
