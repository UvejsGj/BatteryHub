# Test fixtures

Each `.hex` file is one raw report: `# key: value` header lines, then the report as hex
bytes with the report ID first. Whitespace and line breaks between bytes do not matter.

`SonyFixtureTests` runs every file in `Sony/` through the parser its headers name.

| Header | Meaning |
|---|---|
| `pad` | Human-readable pad name |
| `product-id` | Sony PID in hex, picks the parser (e.g. `0CE6`) |
| `connection` | `USB` or `Bluetooth` |
| `source` | `synthetic, ...` for hand-built reports; `captured ...` with date and Probe version for real ones |
| `expect` | `85% Charging`, `100% Full`, `fault` (pad reports a battery fault), or `reject` (parser must refuse it) |
| `note` | Optional: what the pad was doing when captured, or why the report is special |

The Probe prints captured reports in this format. Paste its fixture block into a new file named
`<pad>-<connection>-captured-<state>.hex`, and keep the synthetic files: they cover states that are
hard to reproduce on purpose.
