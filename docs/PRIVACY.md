# Flipr privacy policy

_Last updated: 19 October 2026_

Flipr is an offline file utility for Windows. In short: **your files never leave your PC, and Flipr collects no personal data.**

## What Flipr does with your files

- Everything Flipr does (converting, compressing, resizing, editing, reading text from images, zipping and so on) runs **on your PC**. Your files are never uploaded anywhere.
- Flipr only touches the files you choose with the wheel. Results are saved as **new files next to the original**; the original is never changed.
- "Share" opens the Windows share window, and "Copy to" and "Move to" use folders you pick. Where your files go after that is your choice.

## What Flipr watches

To notice the gesture (dragging a file, then tapping Shift), Flipr watches the mouse buttons and the Shift key while it runs, and listens for its keyboard shortcut, Ctrl + Alt + Space (you can turn the shortcut off in Settings → Shortcuts). It doesn't record, store or send keystrokes, mouse movements or anything you type.

When you use the shortcut, Flipr asks File Explorer which files are selected, so it knows what to work on.

## What Flipr stores

- **Settings** in `%APPDATA%\Flipr\settings.json`: your preferences, such as theme, shortcut and wheel layout.
- **Logs** in `%LOCALAPPDATA%\Flipr\logs`: technical notes such as errors, used for troubleshooting. File names are included only if you turn that on in Settings → Advanced. Logs stay on your PC and are never sent anywhere; you can open their folder from Settings → Advanced.

Uninstalling Flipr removes the app. You can delete the folders above to remove its settings and logs too.

## Network access

- **Microsoft Store version:** Flipr makes no network requests. The Microsoft Store handles updates.
- **Version downloaded from GitHub:** Flipr checks GitHub for new versions (at most every few hours) and downloads updates from GitHub. That request contains only the app's name and version number. You can turn this off in Settings → About.

Flipr has no accounts, ads, analytics, tracking or telemetry, and it doesn't sell or share any data.

## Children

Flipr collects no personal information from anyone, including children.

## Changes

If this policy changes, the new version will be published here with a new date.

## Contact

Questions? Open an issue at <https://github.com/i3sam/Flipr/issues>.
