---
name: csharp-documentation
description: Require XML documentation comments on every C# type and member, including private fields. Use when adding, editing or reviewing C# code in this repository.
---

# C# 注释规范 / C# documentation rules

These rules are mandatory for every C# file in this repository.

## What must be documented

1. Every `class`, `record`, `struct`, `interface`, `enum` and delegate.
2. Every method, constructor, property, event and field — **including private
   ones**, `const` values and local helper records.
3. Every public method parameter that is not self-evident, plus the return value
   when it is not obvious from the name.
4. Non-obvious behaviour in inline comments: units, ranges, ownership, locking,
   side effects, protocol reason codes, and why (not what) the code exists.

## How to write them

- Use XML documentation (`/// <summary>`), not `//` above the member.
- English is the default. For public API, add a second line in Chinese inside
  `<remarks>` so both languages are present:

  ```csharp
  /// <summary>
  /// Accepts one connection for the remote address, consuming a token.
  /// </summary>
  /// <remarks>从令牌桶取一个令牌；超额时返回 false，调用方负责拒绝连接。</remarks>
  /// <param name="remoteAddress">Address as reported by the transport, port included.</param>
  /// <returns><c>true</c> when the connection may proceed.</returns>
  public bool TryAcquire(string remoteAddress)
  ```

- Private members need at least one English sentence; add Chinese when the
  behaviour is subtle.
- Never transliterate Chinese into pinyin (`shanchu`, `huoqu`) — write real
  English or real Chinese. No abbreviations invented ad hoc.
- Comments must match the code. When behaviour changes, update the comment in
  the same commit.
- Do not restate the signature (`/// Gets or sets the name`). Say what the
  member guarantees, what it costs, or what can fail.

## Review checklist

- `grep -n "public\\|private" <file>` returns no undocumented member.
- Every `catch` explains why swallowing is safe.
- Every numeric limit or timeout names its unit.
- Every protocol constant references the MQTT specification section.
