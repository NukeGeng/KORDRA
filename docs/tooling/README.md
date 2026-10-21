# Documentation tooling

```bash
python3 docs/tooling/doccheck.py
python3 docs/tooling/doccheck.py --strict-sources  # after linking real source paths
```

Tool chỉ dùng Python stdlib. Kiểm tra: YAML frontmatter lightweight; module/feature IDs, feature links, địa chỉ markdown nội bộ, required sections, trạng thái, file hiện diện khi strict. Kiểm tra source path trong strict mode chỉ áp dụng cho implemented/verified features, tránh cản trở scaffold chưa có code. Tự động hóa không thay thế source review/test correctness/tenant security audit.

Discovery bỏ qua ba thư mục workspace dành riêng `.git`, `.temp`, `.amr-local` ở mọi cấp và không đi theo directory symlink. Vì vậy bản nháp import/private backup không bị tính như tài liệu chính thức. Các file Markdown khác, kể cả chưa tracked, bị `.gitignore` bỏ qua hoặc nằm trong thư mục ẩn khác, vẫn được kiểm tra; Git ignore không thể dùng để che lỗi tài liệu chính thức.

### Detect changed source without matching docs

The only accepted independent repository boundary is `clients/` declared by `.gitmodules` under [ADR-0013](../decisions/0013-clients-git-submodule.md). Product Markdown discovery prunes that root after validating the exact path/portable URL; arbitrary nested repositories or ignore rules cannot exempt Product documents. The Clients gitlink and `.gitmodules` have FEAT-CLIENTS-001 ownership mappings. Product context identity hashes the indexed pin and ignores the independent frontend worktree; staged pins, Product source, index and HEAD remain tracked. A pre-split snapshot must be refreshed explicitly before reuse; completed execution receipts are preserved.

```sh
python3 docs/tooling/submodulecheck.py                    # declaration + indexed pin; no frontend scan
python3 docs/tooling/submodulecheck.py --require-checkout # isolated CI clone only; exact clean checkout
```

The `clients-pin` CI job checks a fresh remote submodule checkout; the default local Product gate does not require an active frontend worktree to be clean or reset it. Clients CI/tests are owned by its own repository. See [workflow and setup evidence](../engineering/clients-development.md).
```bash
python3 docs/tooling/changeguard.py --base origin/main
```
Guard dùng `git diff <base>...HEAD`, kiểm tra `src/`, `tests/`, `docs/tooling/`, `.codex/`, `.github/workflows/`, `global.json`, `Kordra.slnx` thay đổi, và yêu cầu mỗi source/test/config thuộc một feature có `source_paths` hoặc `test_paths` mapping + feature spec update cùng PR. Cập nhật mapping với file thật và commit spec. Nếu team có generated code, thống nhất explicit exception trong tool và review, không ignore tất cả.

## Reference experiment registry checks
Run `python3 docs/tooling/scopecheck.py`; validates case IDs, phase/mutation safety mapping, owning modules and observe-only sample profile. **Does not run live infrastructure tests.**

## Quality-first AI engineering
Use [AMR v4](../engineering/ai-orchestration/README.md) for bounded Architect design, Luna-led single-agent GOAL execution, durable local checkpoints, native discovery, deterministic gates and separate Codex Auto PR Review/BIR tracking. Requires Python 3.11+ (stdlib only); Codex is optional for offline tests and required for live model probes.

```bash
python3 -m unittest discover -s docs/tooling/tests -v
python3 docs/tooling/amr.py gates
```

Engineering feature specs under `docs/engineering/ai-orchestration/features/` receive the same metadata/ownership/strict path checks as product features. This adds no product bounded context. Private runtime receipts go to ignored `.amr-local/`; never publish them unsanitized.
