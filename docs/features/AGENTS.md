# AGENTS — Features

- Đọc `../../AGENTS.md` trước; trong module đọc `module.md` trước `features/*.md`.
- Không đưa business rule của feature X sang module Y chỉ vì tiện import.
- Feature mới phải có `feature_id`, `module_id`, phase, actor, tenant scope, permission, main/exception flows, message/API/data contract, acceptance/tests, source/test paths.
- Mỗi feature có duy nhất một module owner. Cross-module flow phải ghi contract của mỗi owner.
- Khi sửa handler: update feature doc cùng PR và ghi evidence thay vì tạo ghi chú ở thư mục khác.
