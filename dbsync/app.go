package main

import (
	"context"
	"sync"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/library"
	"gitee.com/wangbenchi66/dbsync/internal/service"
	"gitee.com/wangbenchi66/dbsync/internal/snapshot"
)

// App 是 Wails 绑定层：只做超时/生命周期，不实现业务。
type App struct {
	svc *service.Service
	mu  sync.Mutex
}

func NewApp() *App {
	return &App{svc: service.New()}
}

func (a *App) Version() string { return "0.1.0-m1" }

func (a *App) TestConnection(ep service.Endpoint) (service.ConnInfo, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	return a.svc.TestConnection(ctx, ep)
}

func (a *App) ListDatabases(ep service.Endpoint) ([]string, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	return a.svc.ListDatabases(ctx, ep)
}

func (a *App) Compare(req service.CompareRequest) (*service.CompareView, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
	defer cancel()
	return a.svc.Compare(ctx, req)
}

func (a *App) Plan(runID string, allowDestructive bool) (*service.PlanView, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	return a.svc.Plan(ctx, runID, allowDestructive)
}

func (a *App) CreateSnapshot(ep service.Endpoint, output string, withData bool) (snapshot.Manifest, error) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Minute)
	defer cancel()
	return a.svc.SnapshotCreate(ctx, ep, output, withData)
}

func (a *App) SnapshotLibrary() ([]library.Entry, error) {
	return a.svc.SnapshotLibrary()
}

func (a *App) SnapshotLibraryDir() (string, error) {
	return a.svc.SnapshotLibraryDir()
}

func (a *App) SnapshotImport(path string) (library.Entry, error) {
	return a.svc.SnapshotImport(path)
}

func (a *App) SnapshotDelete(path string) error {
	return a.svc.SnapshotDelete(path)
}

func (a *App) Report(runID, format string) (string, error) {
	return a.svc.Report(runID, format)
}

func (a *App) SaveReport(runID, format, path string) error {
	return a.svc.SaveReport(runID, format, path)
}
