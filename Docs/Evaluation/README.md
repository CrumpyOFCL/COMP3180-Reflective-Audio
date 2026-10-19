# Evaluation

A within-subjects playtest comparing the three acoustic conditions. The results are written up in the Final Project Report under Evaluation.

## Files

| File | What it is |
|---|---|
| `raw_forms_export.csv` | The Microsoft Forms export, exactly as downloaded (Windows-1252 encoding). Anonymous: no names or emails were collected. |
| `analysis.py` | Checks every answer against the form's options and runs all statistics |
| `charts.py` | Draws the report figures |
| `out/clean_responses.csv` | One row per response, with the conditions labelled A, B and C |
| `out/item_stats.csv` | Median, IQR, mean and SD for each item in each condition |
| `out/tests.csv` | Friedman tests, followed by Holm-corrected Wilcoxon tests and effect sizes |
| `out/preferences.csv` | Counts for the "most real" and "most wanted" questions |
| `out/summary.txt` | All of the above in readable form, plus the participants' comments |
| `out/fig_ratings.png`, `out/fig_preferences.png` | Figures 1 and 2 in the report |
| `benchmark/audio-benchmark_P99_*.csv` | The FMOD performance log used in the report, recorded with shots fired in each condition |
| `benchmark/audio-benchmark_P00_*.csv` | An earlier run with no sound playing, which shows idle cost only |
| `benchmark/benchmark.py`, `benchmark/benchmark_summary.csv` | The per-condition summary in Appendix D. Run it with `python benchmark.py <log.csv> benchmark_summary.csv` |

To reproduce the results, run `python analysis.py raw_forms_export.csv out` and then `python charts.py out`. This needs pandas, scipy and matplotlib.

## What to know about the data

- **Versions on the form:** Version 1 was condition A (zone baseline), Version 2 was B (raycast dynamic) and Version 3 was C (Steam Audio).
- **Condition order:** every participant heard the conditions in the same order, A then B then C. The order was not counterbalanced.
- **Response IDs:** participant codes weren't assigned (everyone entered P01), so responses are identified by their Forms ID, R03 to R14. IDs 1 and 2 were test submissions and are not in the export.
- **Timing:** the first form had a fault, so participants re-entered their answers into a corrected copy on 19 October 2026, between 17:50 and 18:16. Completion times in the export therefore don't reflect play time.
- **Benchmarks:** these are short single runs in the Unity editor, and the number of shots wasn't matched across conditions. FMOD's CPU figures don't include Steam Audio's own simulation threads.
- **Glitch item:** reported on its own rather than in the composite score, because almost every answer was 1.
