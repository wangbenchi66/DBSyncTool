package main

import (
	"embed"
	"log"
	"os"
	"syscall"

	"github.com/wailsapp/wails/v3/pkg/application"
)

var (
	user32              = syscall.NewLazyDLL("user32.dll")
	procGetDpiForSystem = user32.NewProc("GetDpiForSystem")
)

// systemZoom 抵消 Windows 显示缩放，避免 WebView 的 CSS 视口被系统 DPI 放大。
func systemZoom() float64 {
	dpi, _, _ := procGetDpiForSystem.Call()
	if dpi == 0 {
		return 1
	}
	return 96 / float64(dpi)
}

//go:embed all:frontend/dist
var assets embed.FS

func main() {
	opts := application.Options{
		Name:        "DBSync",
		Description: "DBSyncTool v2 数据库结构与数据同步工作台",
		Services: []application.Service{
			application.NewService(NewApp()),
		},
		Assets: application.AssetOptions{
			Handler: application.AssetFileServerFS(assets),
		},
	}
	if port := os.Getenv("DBSYNC_CDP_PORT"); port != "" {
		opts.Windows.AdditionalBrowserArgs = []string{"--remote-debugging-port=" + port}
		opts.Windows.WebviewUserDataPath = os.Getenv("TEMP") + `\dbsync-webview-cdp`
	}
	app := application.New(opts)
	app.Window.NewWithOptions(application.WebviewWindowOptions{
		Title:            "DBSync",
		Width:            1440,
		Height:           900,
		MinWidth:         1024,
		MinHeight:        720,
		Zoom:             systemZoom(),
		BackgroundColour: application.NewRGB(247, 248, 250),
		URL:              "/",
	})
	if err := app.Run(); err != nil {
		log.Fatal(err)
	}
}
