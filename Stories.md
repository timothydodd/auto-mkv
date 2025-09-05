# AutoMk User Stories

## Feature: Enhanced Console Feedback

### Epic
As a user running AutoMk, I want clear real-time feedback about what the application is doing so I can monitor progress and understand any issues without having to check log files.

### User Stories

#### Story 1: Real-Time Processing Updates
**As a** user running AutoMk  
**I want** to see real-time updates when discs are detected and processing begins  
**So that** I know the application is working and what disc is being processed  

**Acceptance Criteria:**
- When a disc is inserted, I see a message like "🔍 Detected disc: STAR TREK TNG S5 D1"
- When processing starts, I see "📀 Starting to process disc..."
- When identification completes, I see "✅ Identified as: Star Trek: The Next Generation (TV Series)"

#### Story 2: Ripping Progress Visibility
**As a** user waiting for discs to rip  
**I want** to see progress updates during the ripping process  
**So that** I know how many titles are being ripped and the current status  

**Acceptance Criteria:**
- I see "📀 Ripping 5 titles from disc: STAR TREK TNG S5 D1"
- For each title, I see "Ripping title 1/5: Episode Title (ID: 3)"
- When complete, I see "✅ Ripping completed successfully"

#### Story 3: Important Warnings and Errors
**As a** user running AutoMk  
**I want** to see warnings and errors immediately on the console  
**So that** I can address issues without checking log files  

**Acceptance Criteria:**
- Season mismatches show "⚠️ Season mismatch detected: State shows Season 1, disc shows Season 5"
- Missing drives show "❌ No CD drives found"
- API errors show "⚠️ OMDB API lookup failed for: Title Name"

#### Story 4: File Organization Feedback
**As a** user organizing my media collection  
**I want** to see when files are renamed and moved  
**So that** I know the final organization was successful  

**Acceptance Criteria:**
- I see "📁 Renamed: title_t03.mkv → Star Trek TNG - S05E01 - Redemption.mkv"
- I see "✅ Successfully organized 5 episodes"
- I see "📂 Files moved to: /TV Shows/Star Trek The Next Generation/Season 05/"

#### Story 5: Configurable Console Output
**As a** user who prefers minimal console output  
**I want** to be able to disable console notifications  
**So that** I can run AutoMk quietly while still getting file logs  

**Acceptance Criteria:**
- Setting `ShowConsoleLogging: false` hides all console messages except errors
- Setting `ShowConsoleLogging: true` shows progress and status messages
- Critical errors always show regardless of setting
- File logging continues to work regardless of console setting
