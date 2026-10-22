Market Voyager 1.0.1.9 — bounded persistence and recovery

- 保存失敗後に毎フレーム再試行する問題を修正。通常保存の失敗時は同じスナップショットを別名へ一度だけ退避し、その保存先または両方失敗した旨を表示して自動再試行を停止します。
- 通常設定は即時反映・60秒ごとの変更時保存へ。ログアウト・正常終了・無効化時に未保存分を保存。変更なし・値を元に戻した場合は書き込みません。
- 購入予約・送信前・所持数照合の確定記録を別の耐久ファイルへ分離。保存失敗時は購入を止め、保存復旧後も自動再開しません。巡回開始・再開時はルールIDの参照整合性のため設定を先に確定します。
- 正常な設定バックアップ3世代、破損復元、別名ファイルの次回起動時の検証・明示適用を追加。最新の購入済み数・未確認取引を古い設定バックアップで巻き戻しません。
- 価格確認履歴は各リスト最大512ワールドの最新日時という上限付き形式を維持。旧設定・破損退避・復旧候補を自動削除しません。

Validation: clean Release (0 warnings/errors), 960 core assertions, 548 native checks, 38 persistence checks, 1170 UI/glyph/lifecycle checks, 53 background renders. Synthetic normal fixture: 15 commits / 5,234 logical payload bytes; unchanged intervals and post-shutdown writes zero. Actual game transactions/logout and physical SSD writes are unverified. No live configuration migration or installed DLL replacement performed.
