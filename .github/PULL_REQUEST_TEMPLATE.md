## Summary

<!-- What changed and why, in one or two sentences. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Protocol change
- [ ] Dashboard / UI
- [ ] Documentation
- [ ] Build / deployment / CI
- [ ] Refactor / performance

## Testing

<!-- Commands and scenarios you ran. Include MQTT client QoS and session mode where relevant. -->

```bash
dotnet test NextMqtt.slnx -c Release
./scripts/compliance-check.sh
```

## Checklist

- [ ] Tests cover the change (or explain why not)
- [ ] Documentation and CHANGELOG updated for behaviour changes
- [ ] No forbidden brand tokens or third-party broker content
- [ ] No secrets, tokens or private keys committed
- [ ] Bounded queues, timeouts and cancellation tokens preserved
- [ ] Dashboard types match the API contract
