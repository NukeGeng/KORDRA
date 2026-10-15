# GOAL — DevOps Control Plane

## Vision
Một Control Plane quản lý **ý định release, phê duyệt, điều phối, giám sát, rollback và audit** trên các hạ tầng triển khai sẵn có; không thay thế Kubernetes control plane, Jenkins hay Argo CD.

## Kết quả cần đạt
1. Một tổ chức tạo Project → Application → Environment → Target với ranh giới tenant rõ ràng.
2. Từ artifact immutable (`digest`/hash), người được cấp quyền tạo release và yêu cầu deploy.
3. Policy/approval kiểm soát mọi thay đổi production; phiên bản được duyệt không bị tráo sau phê duyệt.
4. Workflow bền vững, xử lý crash/retry/timeout, phát lệnh tới driver phù hợp, xác minh observed state.
5. Giám sát trạng thái, audit, phát hiện drift và phục hồi theo policy của target.
6. Mở rộng driver mới **không sửa lõi domain** ngoài contract và khả năng (capabilities) đã xác nhận.

## Phạm vi giai đoạn MVP
- Quản lý tenant, membership, role bindings, environment, credential reference.
- Artifact immutability, release, approval, Helm/Kubernetes và Compose/SSH drivers.
- Durable workflow, command/event, agent gateway tối thiểu, deployment logs/status, recovery thủ công có kiểm soát.
- API + UI vận hành; kiểm thử đa tenant và negative authorization bắt buộc.

## Không thuộc MVP
- Viết lại CI engine, Kubernetes scheduler, monitoring engine hoàn chỉnh; auto remediation mọi hạ tầng; multi-region active-active; model training.

## Quality gates (định hướng, không phải số liệu thực đo)
- Không có cross-tenant data access; không bypass production approval.
- Idempotent logical operation khi delivery trùng; restart không làm mất workflow.
- Có test cho failure/rollback/unknown outcome; audit có correlation/actor/target.
- Mỗi feature phải có hồ sơ tại `docs/features/` và traceability trước khi đánh dấu Done.
