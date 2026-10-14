# Improvement: nsdc generated output lacks the nullable directive

- Type: improvement
- Status: done
- Workflow: ../specs/issues.md

## Problem

nsdc 1.3.3 emitted `?` nullable annotations without a `#nullable` directive.
The build failed with CS8669 on any schema that used optional fields. The
build pipeline workaround prepended the directive to every generated file in
the `NsdcCodegen` target. See
[../../specs/codegen-pipeline.md](../../specs/codegen-pipeline.md). The fix
belonged in the needlefish compiler.

## Resolution

nsdc 1.3.4 emits `#nullable enable` in every generated file. The build
pipeline workaround is gone. The compiler stays pinned at 1.3.4 in CI.

## Acceptance criteria

- [x] nsdc emits the `#nullable` directive in generated output, or grows a flag that produces it. nsdc 1.3.4 emits `#nullable enable`.
- [x] The `PrependNullableDirective` task leaves [NsdRpc/NsdRpc.csproj](../../NsdRpc/NsdRpc.csproj).
- [x] The codegen pipeline doc drops the workaround note.
- [x] `NSDC_VERSION` bumps in the workflows and [development.md](../../development.md), same change.
