# Decisions Log

## 2026-03-13: Crash Prevention - App.xaml.cs Hardening

### Problem
Version 1.2.2 deployed with System.Text.Json which caused FileLoadException on net48.
MonitorLogService.SendAsync("app_start") at line 54 crashed BEFORE UpdateService.StartPeriodicCheck() at line 122.
Result: ALL clients dead, unable to self-recover via auto-update.

### Root Cause
1. OnStartup execution order: crash-prone code runs before update check
2. No isolation/try-catch around non-critical service calls
3. Single point of failure: any service crash kills the entire app

### Solution: Defense-in-Depth

#### 1. Reorder OnStartup
- Move `UpdateService.Instance.StartPeriodicCheck()` to run IMMEDIATELY after `HandleSquirrelEvents()` and `GlobalConfig.Initialize()`
- Update check must happen before ANY non-essential service call

#### 2. Isolate all non-critical services in try-catch
Each service initialization wrapped independently:
- MonitorLogService.SendAsync — try/catch (non-critical)
- BustabccLoggingService — try/catch (non-critical)
- ToastPopupService — try/catch (non-critical)
- ConfigService — try/catch (critical but should not kill app)

#### 3. Critical vs Non-Critical classification
**Critical (app must have)**:
- GlobalConfig.Initialize()
- UpdateService (must run for self-healing)
- Mutex check
- MainWindow creation
- BrowserMonitorService (core functionality)
- ConfigService (core functionality)

**Non-Critical (failure should be silent)**:
- MonitorLogService (telemetry)
- BustabccLoggingService (telemetry)
- ToastPopupService (optional feature)
- Desktop shortcut removal
- Hotkey registration

### Team
- Architect: Validate design
- Implementer: Modify App.xaml.cs
- Critic: Review changes

### Files
- App.xaml.cs — primary target

## 2026-03-13: Full Crash Prevention Audit (무인 프로그램 전수 조사)

### Findings Summary
Total: 4 CRITICAL + 11 HIGH

### CRITICAL Fixes (must fix)
1. **ConfigService.cs:58** — Timer `async _ =>` is async void → process crash on exception
2. **UpdateService.cs:28** — Same Timer async void pattern
3. **App.xaml.cs:36** — `async void InitializeConfigAsync()` without try-catch
4. **MainViewModel.cs:95** — `SafeInvoke` Dispatcher.Invoke during app shutdown → crash

### HIGH Fixes (must fix for unattended)
5. **ConfigService.cs:193** — `ConfigReloaded?.Invoke()` subscribers can crash timer thread
6. **LogService.cs:55** — `LogAdded?.Invoke()` without try-catch + recursion risk
7. **ToastPopupService.cs:85** — Timer callback chain not fully protected
8. **MainWindow.xaml.cs:55** — `Dispatcher.Invoke` (sync) → deadlock risk → use BeginInvoke
9. **MainViewModel.cs:276** — `_ = LoadMappingConfigAsync()` UnobservedTaskException
10. **ToastPopupWindow.xaml.cs:82** — NavigationCompleted UI thread not guaranteed
11. **ToastPopupWindow.xaml.cs:167** — DragMove() without state check
12. **MappingConfig.cs SaveToFile** — no try-catch for disk I/O

### Global Defense: UnhandledException + UnobservedTaskException handlers in App.xaml.cs

### Approach
- Add global exception handlers as last-resort safety net
- Fix each crash point individually
- All Timer callbacks: wrap in try-catch
- All event Invoke: wrap in try-catch
- All async void: add try-catch
- SafeInvoke: check HasShutdownStarted
