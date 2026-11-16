# scripts

Checks for the frontend that ESLint and the tests do not cover. Each runs with Node and needs no dependency.

| Script                 | Run by         | What it checks                                                                                                                                                                                                                                                                                                            |
| ---------------------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `check-css-owners.mjs` | `npm run lint` | Every class a feature styles on its own belongs to that feature. Vite bundles every stylesheet into one, so a class name used in two features' stylesheets lets one screen's rules land on the other's elements. Shared styles go in `src/styles/tokens.css`; deliberate sharing is listed in the script with its reason. |
