# Documentation tooling

```bash
python3 docs/tooling/doccheck.py
python3 docs/tooling/doccheck.py --strict-sources  # after linking real source paths
```

Tool chỉ dùng Python stdlib. Kiểm tra: YAML frontmatter lightweight; module/feature IDs, feature links, địa chỉ markdown nội bộ, required sections, trạng thái, file hiện diện khi strict. Kiểm tra source path trong strict mode chỉ áp dụng cho implemented/verified features, tránh cản trở scaffold chưa có code. Tự động hóa không thay thế source review/test correctness/tenant security audit.

### Detect changed source without matching docs
```bash
python3 docs/tooling/changeguard.py --base origin/main
```
Guard dùng `git diff <base>...HEAD`, kiểm tra `src/`, `docs/tooling/`, `.codex/`, `.github/workflows/` thay đổi, và yêu cầu mỗi source/test/config thuộc một feature có `source_paths` hoặc `test_paths` mapping + feature spec update cùng PR. Cập nhật mapping với file thật và commit spec. Nếu team có generated code, thống nhất explicit exception trong tool và review, không ignore tất cả.

## Reference experiment registry checks
Run `python3 docs/tooling/scopecheck.py`; validates case IDs, phase/mutation safety mapping, owning modules and observe-only sample profile. **Does not run live infrastructure tests.**

## Quality-first AI engineering
Use [AMR v2.1](../engineering/ai-orchestration/README.md) for native discovery, verified routing, context handoff, local checks and independent review. Requires Python 3.11+ (stdlib only); Codex is optional for offline tests and required for live probes.

```bash
python3 -m unittest discover -s docs/tooling/tests -v
python3 docs/tooling/amr.py gates
```

Engineering feature specs under `docs/engineering/ai-orchestration/features/` receive the same metadata/ownership/strict path checks as product features. This adds no product bounded context. Private runtime receipts go to ignored `.amr-local/`; never publish them unsanitized.
