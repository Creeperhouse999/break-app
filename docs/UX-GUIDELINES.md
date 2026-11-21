# Break App — UX guidelines

Rules every change to the app must follow. If a change breaks one, fix the change, not the rule.

## Where things live

1. **Organize by what the user wants to do, not by how the code stores it.** A setting goes where a person would look for it. When in doubt, put it in **both** places (e.g. break messages: in the rule editor *and* in Texts).
2. **Every piece of text the user can see is editable in the Texts tab.** That covers break names, break messages, buttons, hints and notification titles.
3. **One concept, one name.** Use the same word everywhere ("break", not "rule" in one place and "reminder" in another).
4. **The most-used settings come first** on a page; rarely used ones are *Advanced* (hidden until the switch is on).

## Look

5. **Everything uses the app theme.** No plain Windows controls inside the app: buttons, menus, dropdowns, the tray menu and dialogs all follow the theme. The self-test checks buttons and menus.
6. **One icon everywhere:** the blue stopwatch tile, for the window, taskbar, tray and settings logo.
7. **Consistent spacing:** rows inside cards; loose buttons always have space above them (the self-test checks this). Inputs in a row have the same height.
8. **Readable contrast.** Text is never black-on-dark or grey-on-grey; break cards are solid, not see-through.
9. **Menus drop down under the button that opened them**, inside the window.

## Behaviour

10. **Changes save automatically.** No Save button; the status line says what happened.
11. **No surprises.** Nothing restarts, closes, locks or pops up without the user expecting it. Dangerous options (TOTAL LOCK, commitment mode) ask "are you sure" first.
12. **Live numbers update live.** Countdowns in the tray, menus and cards tick every second.
13. **Pick, don't type.** Prefer choosing from a list (apps, presets, days) over typing names. Typing stays as a fallback.
14. **Explain in the user's words.** Every setting has a one-line description and an ⓘ tip with an example.

## Before shipping

15. **Run the self-test** (`scripts\run-ui-tests.ps1`) and **look at every screen** (`--ux-tour <dir>`). A test that passes doesn't prove the screen looks right.
16. **Update the README** in the same change as any user-facing feature.
