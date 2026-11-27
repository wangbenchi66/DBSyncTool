package main

import (
	"context"
	"sync"
	"time"

	"gitee.com/wangbenchi66/dbsync/internal/service"
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
