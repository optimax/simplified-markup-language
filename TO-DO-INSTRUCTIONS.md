# TO-DO Instructions for Agents

The file [`TO-DO.md`](TO-DO.md) contains tasks that are to be performed top-to-bottom, in order of their priority. Only when I tell you to, process and implement all TO-DO items that are not marked DONE, DEFER, PAUSE, STOP, or IGNORE, one by one. 

---

**IMPORTANT**: Do not stop mid turn. Continue until you have resolved all tasks in this TO-DO, or until there are no more tasks that are still open. Monitor your own actions and **make sure the user never waits for an update from you longer than 5 minutes**.

---

Always start and re-start after completing each task from the top to pick up the highest priority tasks. As soon as you start *considering* a TO-DO item, mark it [>>WORKING<<]. Don't wait until you start making changes. 

When you encounter a task marked [PAUSE], do not process this task, pause the execution and wait for the user command to resume. Likewise, when you encounter a task marked [STOP], stop the execution (do not process this task) and wait for user commands.

When reading TO-DO, read it in its entirety and analyze which tasks are related, so that you don't make design decisions that you are going to regret further down the line. Understand that some tasks may be related to other tasks and choices you make for one may conflict with the best choices for a future task.

Do not hesitate to ask the user any questions when something is unclear, or when you are faced with a design decision. When you need to ask user a clarifying question, do not pop a modal blocking dialog in the interactive chat. Ask it by placing it in `TO-DO.md`, underneath the relevant task description, formatted in **bold**, and continue with other TO-DOs in the meantime, while waiting for user's answers.

When working on a task, create and work in a temporary git branch off of `main` named such that its relation to the task is obvious. 

When you are truly done and complete with each task, commit, pull `main`, merge your branch into the main reconciling any conflicts that may have arisen the meantime, delete your working git branch, and mark that task [DONE] in this TO-DO itself. 

Do not mark tasks that have active/unanswered questions on them as [DONE] — mark them as [QUESTION]. Make sure answered questions are not formatted bold any more. 

TO-DO list is a live document. Be careful to verify that `TO-DO.md` hasn't changed in the meantime before you make any changes to it, or undertake a new task. The user may be editing it and, for example, moving tasks that were marked done into DONE.md. 

Monitor for TO-DO changes as you are working through a task. When you detect changes, re-read the TO-DO **from the beginning**, as some tasks previously marked as [DONE] may have been rejected, instructions may have changed, etc., and the highest priority tasks are always at the top.

After completing each task, build the solution in Release mode to make the executables available for local copying, then run `publish-locally.bat` to publish.

Proceed until the end, or until you encounter the line `# END-OF-TO-DO`, or `# STOP` whichever comes first. Do NOT work on TO-DO items beyond one of these markers.

---

