---
id: OPS-LOCAL-NOMAD-LAB
status: implemented
owner: NukeGeng
last_reviewed: 2026-10-24
---

# Personal Nomad lab — T09 preparation

## Ownership and scope

FEAT-INV-001/MOD-INV preparation only. NukeGeng authorized a new local 1-vCPU/2-GiB/15-GiB lab. This is not LAB-01, company infrastructure or a production deployment. No company credential is required or imported. Lab setup did not itself accept ADR-0017 or prove Kordra provider/U/C/D integration. NukeGeng subsequently accepted [ADR-0017](../decisions/0017-nomad-workload-observation.md) and its contract separately for T09.2a–d; implementation and lab Keychain activation remain separate. Existing Product checkpoints, Clients/Website, Docker and PostgreSQL are untouched.

## Installed instance

- Lima 2.2.1 using Apple Virtualization.framework; instance kordra-nomad-t09.
- Ubuntu 24.04 ARM64 image pinned to SHA-256 1d6bffe64b848468ac97f821d369a4846d983de1800ccf6b5ec8853e85cefc55.
- Exactly 1 vCPU, 2 GiB RAM and 15 GiB virtual disk; filesystem usable capacity is smaller than virtual disk capacity. Sparse image/cache consume additional host storage; this is not a host-storage quota.
- Nomad 2.0.7 Linux ARM64 archive verified against official SHA-256 19708097585efe0d60234f300b62892fa776ff93ca6ba5fb918360cbd118b71e. Checksum is fetched over HTTPS; no separate release-signature verification is claimed.
- Guest systemd service kordra-nomad-lab, persisted across VM starts; one server and client in the same VM. No Docker engine or Kubernetes installed in the guest.
- HTTPS API on guest 127.0.0.1:4646 forwarded only to Mac 127.0.0.1:14646. No guest mounts of Mac directories, forwarded SSH agent, imported user SSH public keys, propagated proxy environment or automatic forwarding of other guest ports. Lima manages its own SSH access key; host SSH access is available through limactl.
- Private CA/server certificate, TLS on HTTP/RPC, ACL enabled, no anonymous policy grant. Server/RPC/Serf bind only guest loopback. This small personal lab is not an HA/security certification.

VM CPU frequency discovery initially left insufficient schedulable compute. Nomad client cpu_total_compute is set to 1000 MHz as a local scheduler accounting override; actual VM allocation remains one vCPU, and this is not measured hardware performance. Service UMask is 0022 for non-root exec task directory access; credential/account files, CA private key and leaf private key retain explicit 0600 under a root-private guest directory. Fixture tasks use user nobody, not raw_exec.

## Credentials and manual access

Nomad uses ACL tokens rather than a username/password login. Two new tokens are stored privately, never in this document:

| Account | Purpose | Scope |
|---|---|---|
| management | Lab owner maintenance and token issuance | Management, never the Kordra application binding |
| readonly | Candidate Kordra lab credential | Only default namespace list-jobs/read-job; expires after 23 hours |

Host private file: .amr-local/t09/nomad-lab/accounts.json, mode 0600 in the foundation-t03 worktree. Fields management.SecretID and readonly.SecretID contain the actual values. This directory is Git-ignored. Guest backup: /var/lib/kordra-nomad-lab/accounts.json, root-only. Do not paste these values into chat, PRs, logs, shell arguments or tracked docs. Initial setup created no Keychain binding. T09-NOMAD-LOCAL-002 subsequently used only this new lab token in a new UUID isolated store with synthetic owner bindings, verified real native U/C/D/live observation, then deleted the store/rows. No permanent installation binding is active.

Open https://127.0.0.1:14646/ui/ manually and use the readonly SecretID where the UI requests a token. The private lab CA is not installed in macOS system trust: prefer a client using the explicit CA file, rather than disabling certificate verification. UI access may require deliberate browser trust setup for this lab certificate; no trust-store changes were performed automatically.

The explicit CA file is .amr-local/t09/nomad-lab/ca.pem. A CLI used inside the VM can set NOMAD_ADDR=https://127.0.0.1:4646 and NOMAD_CACERT=/var/lib/kordra-nomad-lab/tls/ca.pem. Supply a token through a private environment/secret reader, never an inline command argument. Expired readonly tokens must be deliberately reissued by the owner; no automatic management-token fallback. Nomad's default 24-hour maximum token TTL was retained; the initial attempted 7-day token was rejected and was not issued.

## Start, inspect, stop and rollback

```sh
limactl start kordra-nomad-t09
limactl shell kordra-nomad-t09
limactl shell kordra-nomad-t09 -- sudo systemctl status kordra-nomad-lab
limactl stop kordra-nomad-t09
```

Stopping the named VM releases its running CPU/RAM allocation and retains disk/state. It does not stop Docker/Website/Product services. Deletion is a separate deliberate owner operation: limactl delete kordra-nomad-t09, only after stopping it and deciding whether its private credentials/evidence should be retained. Do not prune all Lima instances, Docker containers or host caches as cleanup.

## Evidence and limitations

Case T09-NOMAD-LOCAL-001 has private immutable configuration/authorization and bounded probe receipts under .amr-local/t09/nomad-lab/. Direct TLS API probes verify token access and denials; a newly created synthetic variable proves direct unauthorized variable read is denied while collection listing can return 200 with filtered empty results. The variable was deleted afterwards. A single synthetic batch sleep job is owned by this case; it has no ports, real payload, secret or external integration. Registration alone does not prove its allocation completed; consult the final receipt for actual state.

Product build/tests were not run for this lab-only setup/design turn, and no native AI/PR review was triggered. A historical goal-status diagnostic reports an owner-path mapping failure; no checkpoint was edited, recovered, replayed or marked complete to hide it. The next Developer should inspect/reconcile the retained checkpoint and prepare the new approved provider scope without replaying completed T07/T08.

This lab uses a self-signed private PKI and local-only networking. For future Product integration, freeze exact installation/trust/config identities, create a new explicit SEC/Keychain binding and record narrowly authorized lab validation. Company network/VPN/token provisioning remains separate.

Sources: [Lima instance creation](https://lima-vm.io/docs/reference/limactl_create/), [Nomad ACL bootstrap](https://developer.hashicorp.com/nomad/docs/secure/acl/bootstrap), [Nomad client configuration](https://developer.hashicorp.com/nomad/docs/configuration/client). Local installed versions, resource settings and probe outcomes above come from this machine's recorded evidence.
