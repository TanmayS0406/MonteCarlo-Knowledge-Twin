# Glossary and FAQ

## Words you will see

| Word | Plain meaning |
|---|---|
| **Monte Carlo simulation** | Predicting by trying: play the project forward many times with random luck, then look at the spread of outcomes. |
| **Iteration / run** | One pretend play-through of the whole project. |
| **P50 / P80 / P95** | The date by which 50% / 80% / 95% of the runs were finished. P80 is a good date to promise. |
| **On-time chance** | The share of runs that finished by the deadline. |
| **Baseline** | The forecast with no changes. |
| **Scenario / what-if** | The forecast after you change something (someone leaves, scope grows…). |
| **Critical path** | The chain of tasks that decides the finish date. Delay anything on it and the project slips. |
| **Criticality index** | How often (as a percentage of runs) a task was on the critical path. 100% means it always decides the date. |
| **Bottleneck** | The role with the most work compared with the people available to do it. |
| **Calibration** | Learning from your finished tickets how wrong estimates usually are (for example, work takes 1.33× the estimate). |
| **Capacity** | The share of someone's day available for project work (meetings and other duties take the rest). |
| **Ramp-up** | How long a new person takes to reach full speed. |
| **Knowledge loss** | The part of a leaver's unfinished work that has to be redone. |
| **Seed** | A number that fixes the random sequence so results are repeatable. |
| **Convergence** | The forecast settling down as more runs are added. |
| **Stale ticket** | A ticket nobody has touched for a while (7 days by default). |
| **Suggestion** | A change the app proposes, such as creating a ticket or closing one. Never applied without approval. |
| **Evidence** | The exact quote, speaker and timestamp behind something the app extracted. |
| **Confidence** | How sure the extractor is (for example *85% sure*). Items below the minimum (45% by default) are dropped. |
| **LocalDB** | A small SQL Server that runs on your own computer and needs no setup. |

---

## FAQ

**Will it change my tickets without asking?**
No. It only *suggests*. A person must press **Accept**. Every acceptance is recorded in the audit log.

**Does my meeting data leave my computer?**
Not by default. The built-in extractor runs offline, and the database is a local file-based SQL Server. Data only goes out if *you* switch on an optional connection such as an AI model, Jira, GitHub or Teams. See [Connecting Your Tools](Connecting-Your-Tools).

**What if the AI gets something wrong?**
That is why every item shows its quote and a confidence score, and why suggestions need approval. A rejected suggestion is remembered, so it does not keep coming back.

**Can I use it with a private or sensitive meeting?**
Yes. Tick **Opt out** when adding the transcript: it is stored, never analysed, and never sent to an AI.

**How long are transcripts kept?**
30 days by default, then the full text is deleted. The decisions, actions and short quotes remain. Change it in **Privacy & settings**.

**Why is the demo forecast so gloomy (4% on time)?**
The demo project deliberately has a tight deadline so you can see the app flag a problem and then try fixes. Change the deadline under **Privacy & settings → Project**.

**Is the forecast a promise?**
No, it is an honest estimate of *uncertainty*. It is only as good as the data going in: current tickets, realistic estimates, the right people. The more tickets you finish, the better it learns how your team really estimates.

**Why does the forecast change a little between runs?**
Random luck. Using the same **seed** gives identical results. Using more iterations makes the answer steadier.

**The 3D view is empty or black.**
Your browser needs WebGL. Turn on hardware acceleration, or try Chrome or Edge.

**How do I start with a clean, empty app?**
Set `"SeedDemoData": false` in `appsettings.json`, then drop the database (see [Getting Started](Getting-Started)).

**Does it track how productive people are?**
No. It tracks work and capacity, not individual performance.
