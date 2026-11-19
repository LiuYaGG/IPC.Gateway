package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
)

func main() {
	log.SetFlags(log.LstdFlags)
	log.SetPrefix("[虚拟倍福PLC] ")

	executable, err := os.Executable()
	if err != nil {
		log.Fatalf("无法确定程序目录: %v", err)
	}
	defaultPath := filepath.Join(filepath.Dir(executable), "points.json")
	configPath := flag.String("config", defaultPath, "点位配置文件路径")
	initConfig := flag.Bool("init", false, "生成示例配置后退出")
	flag.Parse()

	if *initConfig {
		if err := writeDefaultConfig(*configPath); err != nil {
			log.Fatalf("生成配置失败: %v", err)
		}
		fmt.Printf("已生成示例配置: %s\n", *configPath)
		return
	}

	cfg, err := loadConfig(*configPath)
	if os.IsNotExist(err) {
		if err := writeDefaultConfig(*configPath); err != nil {
			log.Fatalf("配置不存在且无法创建: %v", err)
		}
		log.Printf("已自动创建示例配置: %s", *configPath)
		cfg, err = loadConfig(*configPath)
	}
	if err != nil {
		log.Fatalf("加载配置失败: %v", err)
	}

	store, err := newStore(cfg)
	if err != nil {
		log.Fatalf("点位配置无效: %v", err)
	}
	server := NewAdsServer(cfg, store)
	if err := server.Start(); err != nil {
		log.Fatalf("启动失败: %v", err)
	}
	log.Printf("监听 %s:%d，AMS NetId=%s，ADS Port=%d", cfg.Host, cfg.Port, cfg.AmsNetID, cfg.AdsPort)
	log.Printf("已加载 %d 个点位；修改 points.json 后重启本程序生效", len(cfg.Points))
	for _, point := range cfg.Points {
		log.Printf("  %-32s %s", point.Name, point.Type)
	}
	log.Print("按 Ctrl+C 停止虚拟 PLC")

	signals := make(chan os.Signal, 1)
	signal.Notify(signals, os.Interrupt, syscall.SIGTERM)
	<-signals
	log.Print("正在停止...")
	if err := server.Close(); err != nil {
		log.Printf("停止时发生错误: %v", err)
	}
}

func writeDefaultConfig(path string) error {
	if _, err := os.Stat(path); err == nil {
		return fmt.Errorf("文件已存在，未覆盖: %s", path)
	}
	data, err := json.MarshalIndent(defaultConfig(), "", "  ")
	if err != nil {
		return err
	}
	data = append(data, '\n')
	return os.WriteFile(path, data, 0644)
}
