# Security policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 0.1.x   | Yes       |
| < 0.1   | No        |

Pre-1.0 releases receive security fixes on the latest minor line.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Use GitHub's private vulnerability reporting:

<https://github.com/cn-cam/next-mqtt/security/advisories/new>

Include:

- affected version or commit;
- deployment mode (single node, cluster, container, Kubernetes);
- reproduction steps or a proof of concept;
- impact assessment;
- any suggested fix.

We aim to acknowledge reports within 3 business days, provide a remediation
plan within 10 business days, and publish an advisory after a fix is
available. Credit is given unless you prefer to remain anonymous.

## Hardening checklist for operators

- Change the bootstrap admin password before exposing the dashboard.
- Terminate MQTT/TLS and HTTPS with valid certificates; never ship the
  development certificate to production.
- Keep the cluster secret and admin API signing key in a secret manager.
- Restrict the dashboard and cluster ports to trusted networks.
- Enable authentication and deny-by-default ACLs for device traffic.
- Run containers as the non-root user provided by the image.
- Keep `NEXTMQTT_*` secrets out of images, logs and crash dumps.
