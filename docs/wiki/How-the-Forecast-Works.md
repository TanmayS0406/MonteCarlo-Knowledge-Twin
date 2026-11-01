# How the Forecast Works

No maths degree needed. If you can imagine rolling dice, you can understand this.

## The idea in one minute

Ask a team *"how long will this take?"* and you get **one number**: "3 days". But everyone knows the real answer is *"probably 3, maybe 5, occasionally 8"*. A single number hides all of that.

**Monte Carlo simulation** (named after the casino) handles uncertainty by *trying it out* many times:

1. Pretend to run the whole project from today.
2. Let luck play out: some tasks overrun, someone is off sick.
3. Write down the day the project finished.
4. **Do that 1,000 times** (or more).

You end up with 1,000 possible finish dates. If 800 of them are on or before 24 Dec, then *"80% chance we're done by 24 Dec"* is an honest statement.

## What happens in each simulated run

| Step | In plain words |
|---|---|
| **Roll the dice on estimates** | Each task gets a random "how wrong was the estimate" factor. Some tasks come in early, many run over. |
| **Learn from history** | The factors are not made up. The app looks at your *finished* tickets (estimate versus actual days). In the demo, work takes about **1.33× the estimate** on average, and that is built in. Each role (Backend, Frontend, QA…) gets its own numbers. |
| **People are sometimes away** | Each person has a small random chance of being out on any given day (3% by default). |
| **Play it day by day** | Each working day, every available person picks the most important task they can do and makes progress according to their capacity. Tasks wait for the ones they depend on. |
| **Handle the awkward bits** | New hires start slowly and slow their mentors a little. Someone leaving takes part of their in-progress knowledge with them. Doing work outside your role is slower. |
| **Note the finish day** | When the last task is done, write the day down. Repeat. |

## Reading the results

Sort all the finish dates from earliest to latest:

| Label | Means | Use it for |
|---|---|---|
| **P50** | Half of runs finish by this date. | A coin-flip date. Too risky for promises. |
| **P80** | 80% of runs finish by this date. | A sensible date to tell stakeholders. |
| **P95** | 95% of runs finish by this date. | Worst-case planning. |
| **On time** | The share of runs that beat the deadline. | The honest answer to *"will we make it?"* |

*Example from the demo data:* P50 **22 Dec**, P80 **24 Dec**, P95 **28 Dec**. The deadline is 16 Dec, so only **4%** of runs make it. The app also names the **bottleneck** (the role with too much work for too few people) and the **critical path** (the chain of tasks that sets the finish date).

## How "what-if" works fairly

When you change something (say a developer leaves), the app runs the **baseline** and the **scenario** using the **same random luck**. Run #17 gets the same estimate errors and the same sick days in both. So any difference you see comes from your change, not from lucky dice. (Statisticians call this *common random numbers*.)

The scenarios you can try:

| Scenario | Question it answers |
|---|---|
| **Person leaves** | What if Rohan leaves on day 10? |
| **New hire** | What if we add a contractor, who needs 10 days to get up to speed? |
| **Scope change** | What if the client adds 20% more work? |
| **Remove a task** | What if we drop this feature? |
| **Priority swap** | What if we do this one first? |
| **More meetings** | What if everyone loses 15% of their day to meetings? |
| **Client delay** | What if the client is late with credentials for 10 days? |

## Why a "seed" number?

The **seed** just fixes the "random" sequence so the same inputs always give the same answer. Change it (**New seed**) to check that your conclusions do not depend on one lucky sequence.

## How many runs are enough?

The **Convergence** chart on the [3D Monte Carlo](3D-Monte-Carlo-Guide) page shows the answer settling down as runs accumulate. When the lines go flat, more runs will not change the story. A thousand is usually plenty.

Next: [Reading the 3D Monte Carlo View](3D-Monte-Carlo-Guide)
