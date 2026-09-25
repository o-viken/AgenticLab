---
name: whoami
description: Get the username of the currently logged in user on a Windows machine using the terminal.
---

# Get the current username (Windows)

Use this skill when the user asks who they are, what their username is, or which account is currently
logged in on a Windows machine.

## Steps

1. Run the following command with the `RunCommand` tool:

   ```
   powershell -NoProfile -Command "$env:USERNAME"
   ```

   `$env:USERNAME` returns the name of the currently logged in user.

2. If the user wants the full account name including the domain, use:

   ```
   powershell -NoProfile -Command "whoami"
   ```

   `whoami` returns the value in the form `DOMAIN\username`.

3. Report the value from the command's output back to the user. Do not invent or guess the username —
   only state what the command returned.

## Notes

- This uses the `powershell` command, which is on the allowlist. Run a single command per call; do not
  chain commands with shell operators.
