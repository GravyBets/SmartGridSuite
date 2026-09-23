# SmartGridSuite

## TOP Change workflow

On branch `feature/dispatch-top-change-workflow`, **Request TOP Change** is in the
Site Dashboard's **TOP Access** card. Dispatch manages requests from **Tasks → TOP Change**.
Run the [database setup and test checklist](docs/top-change-workflow.md) before deploying this version.

## VM restart password setup

Follow the [step-by-step VM password setup guide](deploy/maintenance/README.md#step-by-step-set-the-restart-password-on-the-vm) when you are back at work.
It covers pulling the testing branch, uploading the helper, setting/changing the shared password, and verifying setup without restarting the API.
The app remains on HTTP; use a unique restart-only password because it is sent unencrypted.


To-Do List

Administration

	Admin Shell
		-Fix the Left Navigation Pane Text sizing, shadows, etc. 
		-Add Home Button (goes back to start screen)
		-Correct weird card shadow between all the pane cards (Header, Main, Status)
	Technicians
		-Refresh after hitting Save on Add Tech Window
		-Add a red Delete button with an Are you sure? Red Button(Deletes tech entry)
		-Punctuation in header
	Trucks
		-Punctuation in header

Dispatch

	Dispatch Shell
		-Fix everything to do with the Left Navigation Pane
		-Either remove the top ribbon, or brainstorm about quick action buttons to add up there. 
		-Add Home button at bottom of nav pane to take back to start screen

	Dashboard
		-All the things
	Tasks
		-All the things
	Tickets
		-Filter titles need a font fix
		-Add Date filter for Ticket Creation Date		
		-Select Multiple Status Filters at one time
		-Filter out Closed Tickets by default
		-Clear Filters Button
		-Add Created Date Column
		-Add Total Numbers somewhere (Similar to SAP Queue Summary)
		-Datagrid Header text needs fixing (lower the description below the title header)
		-Actual datagrid rows need style matching. 
		-Need to remove copy buttons from the datagrid rows. 
		-ASK JIM IF DATE FILTER SHOULD BE BY LAST ACTIVITY OR WHEN CREATED!
	Ticket Details
		-Ticket details pane copy buttons needs to show "Copied!"
		-Ticket details pane - find a way to compact all the ticket info so the notes area is a bit larger. Maybe 2 columns of data instead of a single column stacked. 
	Import SAP Queue
		-Change status window after importing. (Shouldnt have Already Exists)
		-Show when last import was (Date/Time)
		-Remove "Row" Column
		-Re-Order columns (Site, Notification, Work Order, Description, Notif. Date, Status, Message)
		-Add exception warning if excel spreadsheet is open. App crashes if it is. 

	Technicians
		-Rename Pane as "Truck Assignments"
		-PLEASE FIX THIS GODFORSAKEN PANE. So ugly. Figure it out dummy. 
	
