---
name: get-date
description: Get the current date and time on a Windows machine using the terminal.
---

# Get the current date and time (Windows)

Use this skill when the user asks for the current date, the current time, or both, on a Windows
machine.

## Steps

1. Run the following command with the `RunCommand` tool:

   ```
   powershell -NoProfile -Command "Get-Date"
   ```

   `Get-Date` returns both the current date and the current time in one line, so a single call covers
   either request.

2. If the user wants a specific format, pass a format string to `Get-Date`, for example:

   - Date only (ISO): `powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"`
   - Time only (24h): `powershell -NoProfile -Command "Get-Date -Format HH:mm:ss"`
   - Date and time (ISO): `powershell -NoProfile -Command "Get-Date -Format s"`

3. Report the value from the command's output back to the user. Do not invent or guess the date or
   time — only state what the command returned.

## Notes

- This uses the `powershell` command, which is on the allowlist. Run a single command per call; do not
  chain commands with shell operators.
