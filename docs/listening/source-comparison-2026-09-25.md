# Listening source comparison, 25 Sep 2026

Every published section compared against the owner's original recordings (Desktop\Listening): Jahshan = Atlas, Benchmark = Nova.

Method (all read-only, run locally because the source recordings exist only on the owner's PC):
1. **Location:** each production file is found inside its source recording by 20 Hz energy-envelope correlation (a speech-anchored head window and tail window must agree; all 159 located sections have r >= 0.9, and every file is one contiguous span).
2. **Cut loudness:** at every cut, the source's energy within +/-40 ms is compared with the local speech level. A cut *splits* a sound only when there is speech-level sound on both sides. Calibrated on known cases: the old ST1/ST3 cuts register as split (-0.7/-5.9 dB) and the repaired ones as clean (-70/-89 dB, 0.30 s before the first sound).
3. **Words:** faster-whisper transcribes about 6 s of source either side of every cut. This identifies which words sit at each cut and whether any source speech falls between sections.

Verdicts: **clean** = the cut is in a pause and no word is split; **correction needed** = source-confirmed split, repair files prepared and awaiting owner go-ahead; **no source** = the original recording is not in the folder.

| Paper | Section | Source span (s) | Verdict | Note |
|---|---|---|---|---|
| Nova Test 20 | A1 | - | no source | original recording not in the folder |
| Nova Test 20 | A2 | - | no source | original recording not in the folder |
| Nova Test 20 | B | - | no source | original recording not in the folder |
| Nova Test 20 | C1 | - | no source | original recording not in the folder |
| Nova Test 20 | C2 | - | no source | original recording not in the folder |
| Nova Test 19 | A1 | - | no source | original recording not in the folder |
| Nova Test 19 | A2 | - | no source | original recording not in the folder |
| Nova Test 19 | B | - | no source | original recording not in the folder |
| Nova Test 19 | C1 | - | no source | original recording not in the folder |
| Nova Test 19 | C2 | - | no source | original recording not in the folder |
| Nova Test 18 | A1 | 0.00-326.77 | clean |  |
| Nova Test 18 | A2 | 326.80-626.59 | clean |  |
| Nova Test 18 | B | 626.60-1216.30 | clean |  |
| Nova Test 18 | C1 | 1216.30-1652.66 | clean |  |
| Nova Test 18 | C2 | 1652.65-2076.01 | clean |  |
| Nova Test 17 | A1 | 0.00-326.61 | clean |  |
| Nova Test 17 | A2 | 326.60-612.90 | clean |  |
| Nova Test 17 | B | 612.90-1195.71 | clean |  |
| Nova Test 17 | C1 | 1195.75-1593.46 | clean |  |
| Nova Test 17 | C2 | 1593.45-2007.47 | clean |  |
| Nova Test 16 | A1 | 0.00-305.09 | clean |  |
| Nova Test 16 | A2 | 305.10-619.18 | clean |  |
| Nova Test 16 | B | 619.20-1191.37 | clean |  |
| Nova Test 16 | C1 | 1191.40-1572.41 | clean |  |
| Nova Test 16 | C2 | 1572.40-1963.49 | clean |  |
| Nova Test 15 | A1 | - | no source | original recording not in the folder |
| Nova Test 15 | A2 | - | no source | original recording not in the folder |
| Nova Test 15 | B | - | no source | original recording not in the folder |
| Nova Test 15 | C1 | - | no source | original recording not in the folder |
| Nova Test 15 | C2 | - | no source | original recording not in the folder |
| Nova Test 14 | A1 | 0.00-339.32 | clean |  |
| Nova Test 14 | A2 | 339.30-670.12 | clean |  |
| Nova Test 14 | B | 670.15-1269.78 | clean |  |
| Nova Test 14 | C1 | 1269.75-1661.67 | clean |  |
| Nova Test 14 | C2 | 1661.70-2138.40 | clean |  |
| Nova Test 13 | A1 | 0.00-380.44 | clean |  |
| Nova Test 13 | A2 | 380.45-738.61 | clean |  |
| Nova Test 13 | B | 738.65-1291.68 | clean |  |
| Nova Test 13 | C1 | 1291.65-1669.65 | clean |  |
| Nova Test 13 | C2 | 1669.65-1990.46 | clean |  |
| Nova Test 12 | A1 | 0.00-330.84 | clean |  |
| Nova Test 12 | A2 | 330.85-642.74 | clean |  |
| Nova Test 12 | B | 642.75-1170.44 | clean |  |
| Nova Test 12 | C1 | 1170.45-1592.32 | clean |  |
| Nova Test 12 | C2 | 1592.35-2029.49 | clean |  |
| Nova Test 11 | A1 | 0.00-331.60 | clean |  |
| Nova Test 11 | A2 | 331.60-648.37 | clean |  |
| Nova Test 11 | B | 648.40-1146.96 | clean |  |
| Nova Test 11 | C1 | 1146.95-1558.81 | clean |  |
| Nova Test 11 | C2 | 1558.85-1984.68 | clean |  |
| Nova Test 10 | A1 | 0.00-301.04 | clean |  |
| Nova Test 10 | A2 | 301.05-643.81 | clean |  |
| Nova Test 10 | B | 643.85-1192.54 | clean |  |
| Nova Test 10 | C1 | 1192.55-1626.48 | clean |  |
| Nova Test 10 | C2 | 1626.45-2081.32 | clean |  |
| Nova Test 9 | A1 | 0.00-356.79 | clean |  |
| Nova Test 9 | A2 | 356.80-668.24 | clean |  |
| Nova Test 9 | B | 668.25-1196.80 | clean |  |
| Nova Test 9 | C1 | 1196.80-1598.67 | clean |  |
| Nova Test 9 | C2 | 1598.70-2063.26 | clean |  |
| Nova Test 8 | A1 | 0.00-462.09 | clean |  |
| Nova Test 8 | A2 | 462.10-879.91 | clean |  |
| Nova Test 8 | B | 879.90-1424.96 | clean |  |
| Nova Test 8 | C1 | 1425.00-1814.62 | clean |  |
| Nova Test 8 | C2 | 1814.65-2222.29 | clean |  |
| Nova Test 7 | A1 | 0.00-363.96 | clean |  |
| Nova Test 7 | A2 | 363.95-648.89 | clean |  |
| Nova Test 7 | B | 648.90-1183.04 | clean |  |
| Nova Test 7 | C1 | 1183.10-1532.87 | clean |  |
| Nova Test 7 | C2 | 1532.85-1902.16 | clean |  |
| Nova Test 6 | A1 | 0.00-324.33 | clean |  |
| Nova Test 6 | A2 | 324.35-650.92 | clean |  |
| Nova Test 6 | B | 650.95-1186.53 | clean |  |
| Nova Test 6 | C1 | 1186.50-1591.11 | clean |  |
| Nova Test 6 | C2 | 1591.15-2004.54 | clean |  |
| Nova Test 5 | A1 | 0.00-352.75 | clean |  |
| Nova Test 5 | A2 | 352.75-672.86 | clean |  |
| Nova Test 5 | B | 672.90-1193.93 | clean |  |
| Nova Test 5 | C1 | 1193.90-1587.48 | clean |  |
| Nova Test 5 | C2 | 1587.50-2047.03 | clean |  |
| Nova Test 4 | A1 | 0.00-336.04 | clean |  |
| Nova Test 4 | A2 | 336.05-646.92 | clean |  |
| Nova Test 4 | B | 646.95-1167.79 | clean |  |
| Nova Test 4 | C1 | 1167.80-1535.71 | clean |  |
| Nova Test 4 | C2 | 1535.70-1955.30 | clean |  |
| Atlas Sample Test 15 | A1 | 0.00-240.55 | clean |  |
| Atlas Sample Test 15 | A2 | 240.55-506.41 | clean |  |
| Atlas Sample Test 15 | B | 0.00-479.74 | clean |  |
| Atlas Sample Test 15 | C1 | 0.00-430.61 | clean |  |
| Atlas Sample Test 15 | C2 | 430.60-790.32 | clean |  |
| Nova Test 3 | A1 | 0.00-373.74 | clean |  |
| Nova Test 3 | A2 | 373.75-677.43 | clean |  |
| Nova Test 3 | B | 677.45-1164.67 | clean |  |
| Nova Test 3 | C1 | 1164.70-1585.86 | clean |  |
| Nova Test 3 | C2 | 1585.85-2026.23 | clean |  |
| Atlas Sample Test 14 | A1 | 0.00-259.05 | clean |  |
| Atlas Sample Test 14 | A2 | 259.05-540.53 | clean |  |
| Atlas Sample Test 14 | B | 0.00-463.49 | clean |  |
| Atlas Sample Test 14 | C1 | 0.00-428.30 | clean |  |
| Atlas Sample Test 14 | C2 | 428.30-811.78 | clean |  |
| Atlas Sample Test 13 | A1 | 0.00-271.85 | **correction needed** | A1/A2 cut is inside the dialogue ("working, | okay?"): Extract 1's last word plays at the start of A2. Fix: cut at 272.66 s (pause 272.21-272.96). |
| Atlas Sample Test 13 | A2 | 271.85-533.42 | **correction needed** | see A1 |
| Atlas Sample Test 13 | B | 0.00-361.48 | clean |  |
| Atlas Sample Test 13 | C1 | 0.00-370.75 | clean |  |
| Atlas Sample Test 13 | C2 | 370.75-713.22 | clean |  |
| Nova Test 2 | A1 | 0.00-452.74 | clean |  |
| Nova Test 2 | A2 | 452.75-839.89 | clean |  |
| Nova Test 2 | B | 839.90-1342.80 | clean |  |
| Nova Test 2 | C1 | 1342.80-1719.58 | clean |  |
| Nova Test 2 | C2 | 1719.60-2165.00 | clean |  |
| Atlas Sample Test 12 | A1 | 0.00-226.23 | clean |  |
| Atlas Sample Test 12 | A2 | 226.25-493.22 | clean |  |
| Atlas Sample Test 12 | B | 0.00-390.66 | clean |  |
| Atlas Sample Test 12 | C1 | 0.00-292.38 | clean |  |
| Atlas Sample Test 12 | C2 | 292.40-598.60 | clean |  |
| Nova Test 1 | A1 | 0.00-352.41 | clean |  |
| Nova Test 1 | A2 | 352.40-669.11 | clean |  |
| Nova Test 1 | B | 669.15-1218.39 | clean |  |
| Nova Test 1 | C1 | 1218.40-1643.59 | clean |  |
| Nova Test 1 | C2 | 1643.60-2042.89 | clean |  |
| Atlas Sample Test 11 | A1 | 0.00-342.50 | clean |  |
| Atlas Sample Test 11 | A2 | 342.50-642.81 | clean |  |
| Atlas Sample Test 11 | B | 0.00-458.62 | clean |  |
| Atlas Sample Test 11 | C1 | 0.00-386.28 | clean |  |
| Atlas Sample Test 11 | C2 | 386.30-943.07 | clean |  |
| Atlas Sample Test 10 | A1 | 0.00-376.31 | clean |  |
| Atlas Sample Test 10 | A2 | 376.40-706.53 | clean |  |
| Atlas Sample Test 10 | B | 0.00-490.32 | clean |  |
| Atlas Sample Test 10 | C1 | 0.00-448.29 | clean |  |
| Atlas Sample Test 10 | C2 | 448.40-1026.37 | clean |  |
| Atlas Sample Test 9 (Q37–42 unavailable) | A1 | 0.00-246.17 | clean |  |
| Atlas Sample Test 9 (Q37–42 unavailable) | A2 | 246.15-545.55 | clean |  |
| Atlas Sample Test 9 (Q37–42 unavailable) | B | 0.00-476.55 | clean |  |
| Atlas Sample Test 9 (Q37–42 unavailable) | C1 | 0.00-400.00 | clean |  |
| Atlas Sample Test 8 | A1 | - | no source | original recording not in the folder (ST8 resolved by the 23 Sep multi-device check) |
| Atlas Sample Test 8 | A2 | - | no source | original recording not in the folder (ST8 resolved by the 23 Sep multi-device check) |
| Atlas Sample Test 8 | B | - | no source | original recording not in the folder (ST8 resolved by the 23 Sep multi-device check) |
| Atlas Sample Test 8 | C1 | - | no source | original recording not in the folder (ST8 resolved by the 23 Sep multi-device check) |
| Atlas Sample Test 8 | C2 | - | no source | original recording not in the folder (ST8 resolved by the 23 Sep multi-device check) |
| Atlas Sample Test 7 | A1 | 0.00-352.50 | clean |  |
| Atlas Sample Test 7 | A2 | 352.60-712.39 | clean |  |
| Atlas Sample Test 7 | B | 0.00-483.03 | clean |  |
| Atlas Sample Test 7 | C1 | 0.00-441.45 | clean |  |
| Atlas Sample Test 7 | C2 | 441.20-1078.67 | clean |  |
| Atlas Sample Test 6 | A1 | 0.00-333.08 | clean |  |
| Atlas Sample Test 6 | A2 | 333.45-632.96 | clean |  |
| Atlas Sample Test 6 | B | 0.00-525.61 | clean |  |
| Atlas Sample Test 6 | C1 | 0.00-475.31 | clean |  |
| Atlas Sample Test 6 | C2 | 475.30-1054.48 | clean |  |
| Atlas Sample Test 5 | A1 | 37.30-394.35 | **correction needed** | A1/A2 cut splits "Extract 2" (0.37 s of "Extract" in A1). Fix: cut at 393.73 s (pause 386.40-394.03). |
| Atlas Sample Test 5 | A2 | 394.45-698.51 | **correction needed** | see A1 |
| Atlas Sample Test 5 | B | 718.60-1283.47 | clean |  |
| Atlas Sample Test 5 | C1 | 1303.10-1794.50 | clean |  |
| Atlas Sample Test 5 | C2 | 1794.60-2248.26 | clean |  |
| Atlas Sample Test 4 | A1 | 37.80-343.17 | clean |  |
| Atlas Sample Test 4 | A2 | 343.20-623.41 | clean |  |
| Atlas Sample Test 4 | B | 645.25-1142.30 | clean |  |
| Atlas Sample Test 4 | C1 | 1162.55-1695.37 | clean |  |
| Atlas Sample Test 4 | C2 | 1695.45-2114.55 | clean |  |
| Atlas Sample Test 3 | A1 | 37.65-340.22 | clean | repaired 24-25 Sep (Extract 2 onset + final word restored) |
| Atlas Sample Test 3 | A2 | 340.20-636.00 | clean | repaired 24-25 Sep (Extract 2 onset + final word restored) |
| Atlas Sample Test 3 | B | 651.55-1149.85 | clean |  |
| Atlas Sample Test 3 | C1 | 1170.10-1594.41 | clean |  |
| Atlas Sample Test 3 | C2 | 1594.50-2033.56 | clean |  |
| Atlas Sample Test 2 | A1 | 0.00-372.07 | clean |  |
| Atlas Sample Test 2 | A2 | 372.20-758.75 | clean |  |
| Atlas Sample Test 2 | B | 0.00-513.65 | clean |  |
| Atlas Sample Test 2 | C1 | 0.00-466.51 | **correction needed** | C1/C2 cut splits "Now, look at Extract 2" (0.09 s of "Now" in C1). Fix: cut at 466.16 s (pause 458.55-466.46). |
| Atlas Sample Test 2 | C2 | 466.60-1064.50 | **correction needed** | see C1 |
| Atlas Sample Test 1 | A1 | 0.00-380.30 | clean | repaired 24-25 Sep (Extract 2 onset restored) |
| Atlas Sample Test 1 | A2 | 380.30-661.24 | clean | repaired 24-25 Sep (Extract 2 onset restored) |
| Atlas Sample Test 1 | B | 0.00-527.15 | clean |  |
| Atlas Sample Test 1 | C1 | 0.00-453.94 | clean |  |
| Atlas Sample Test 1 | C2 | 454.05-978.71 | clean |  |
| Atlas Kaplan Listening Practice Test | A1 | 0.00-358.46 | **correction needed** | A1/A2 cut splits "Now look at the notes for | extract 2". Fix: cut at 357.21 s (pause 350.45-357.51). |
| Atlas Kaplan Listening Practice Test | A2 | 358.45-678.20 | **correction needed** | see A1 |
| Atlas Kaplan Listening Practice Test | B | 678.25-1206.28 | clean |  |
| Atlas Kaplan Listening Practice Test | C1 | 1206.25-1549.55 | **correction needed** | C1/C2 cut splits "Now, turn over and look at | extract 2". Fix: cut at 1547.26 s (pause 1541.60-1547.56). |
| Atlas Kaplan Listening Practice Test | C2 | 1549.65-1969.46 | **correction needed** | see C1 |

## Observations with no change made

- **Not in any section, and not exam content:** the spoken part transitions "That is the end of Part A. Now look at Part B." and "That is the end of Part B. Now look at Part C." (about 20 s each), the opening test introduction (Atlas ST3/4/5 whole-test recordings), and the closing "That is the end of the listening test." (Nova, ST3/4/5). The platform provides its own section transitions. Left as is; the owner can decide whether to add them.
- **E2Language recordings (ST11-15):** the intro before Extract 2 says "questions 1 to 12", as the original recording does. The audio-to-question mapping was verified correct (ST13: A1 = Mrs Brownstone, A2 = Mrs Chambers).
- **Tight but whole cuts (0.05-0.09 s before the first sound):** ST13 C2, ST14 C2, ST4 C2 end, Nova 3/8/14 C2 end, Nova 5 B end. No word is split; no change.
- **Non-speech sound at part changes (tone/noise) at Nova A2/B, B/C1 and Kaplan B:** every adjacent word is complete ("...sample test paper."). No change.
