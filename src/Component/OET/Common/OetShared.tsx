"use client";

import Link from "next/link";
import type { CSSProperties, ReactNode } from "react";
import { useEffect, useState } from "react";
import {
  Activity,
  Calendar,
  DataTransferBoth,
  Folder,
  HomeAlt,
  LeaderboardStar,
  MessageText,
  PageEdit,
  Reports,
  Settings,
  Shield,
  UserCircle,
  WarningCircle,
  XrayView,
} from "iconoir-react";
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { getAsyncWorkflowMeta } from "@/lib/oet/async-status";
import { getOetVisualConfig } from "@/lib/oet/visual";
import type {
  AsyncWorkflowStatus,
  OetVisualAccent,
  OetVisualActivityItem,
  OetVisualChart,
} from "@/types/oet";

interface PageAction {
  href: string;
  label: string;
}

interface OetPageShellProps {
  action?: PageAction;
  children: ReactNode;
  description?: string;
  icon: React.ElementType;
  layoutMode?: "full" | "lean";
  mainTitle: string;
  path: string[];
  title: string;
}

interface MetricItem {
  delta?: string;
  helper?: string;
  icon?: string;
  label: string;
  tone?: OetVisualAccent;
  value: string | number;
}

interface SectionCardProps {
  children: ReactNode;
  description?: string;
  title: string;
}

const tonePalette: Record<
  OetVisualAccent,
  {
    background: string;
    badge: string;
    border: string;
    glow: string;
    soft: string;
    text: string;
  }
> = {
  danger: {
    background:
      "linear-gradient(135deg, rgba(255, 88, 88, 0.18), rgba(255, 255, 255, 0.98))",
    badge: "light-danger",
    border: "rgba(255, 88, 88, 0.28)",
    glow: "rgba(255, 88, 88, 0.18)",
    soft: "#fff2f2",
    text: "#a91d3a",
  },
  info: {
    background:
      "linear-gradient(135deg, rgba(58, 123, 213, 0.16), rgba(255, 255, 255, 0.98))",
    badge: "light-info",
    border: "rgba(58, 123, 213, 0.22)",
    glow: "rgba(58, 123, 213, 0.14)",
    soft: "#f1f7ff",
    text: "#1258a8",
  },
  primary: {
    background:
      "linear-gradient(135deg, rgba(90, 72, 255, 0.18), rgba(255, 255, 255, 0.98))",
    badge: "light-primary",
    border: "rgba(90, 72, 255, 0.24)",
    glow: "rgba(90, 72, 255, 0.16)",
    soft: "#f5f4ff",
    text: "#4b3ef6",
  },
  success: {
    background:
      "linear-gradient(135deg, rgba(31, 186, 110, 0.16), rgba(255, 255, 255, 0.98))",
    badge: "light-success",
    border: "rgba(31, 186, 110, 0.24)",
    glow: "rgba(31, 186, 110, 0.15)",
    soft: "#f3fff9",
    text: "#128356",
  },
  warning: {
    background:
      "linear-gradient(135deg, rgba(255, 182, 55, 0.2), rgba(255, 255, 255, 0.98))",
    badge: "light-warning",
    border: "rgba(255, 182, 55, 0.28)",
    glow: "rgba(255, 182, 55, 0.15)",
    soft: "#fff9ef",
    text: "#b86a00",
  },
};

function getTone(seed: string): OetVisualAccent {
  const normalized = seed.toLowerCase();

  if (
    normalized.includes("warning") ||
    normalized.includes("risk") ||
    normalized.includes("queue")
  ) {
    return "warning";
  }

  if (
    normalized.includes("writing") ||
    normalized.includes("content") ||
    normalized.includes("publish")
  ) {
    return "primary";
  }

  if (
    normalized.includes("speaking") ||
    normalized.includes("transcript") ||
    normalized.includes("audio")
  ) {
    return "info";
  }

  if (
    normalized.includes("readiness") ||
    normalized.includes("plan") ||
    normalized.includes("success")
  ) {
    return "success";
  }

  if (
    normalized.includes("review") ||
    normalized.includes("expert") ||
    normalized.includes("sla")
  ) {
    return "danger";
  }

  const tones: OetVisualAccent[] = [
    "primary",
    "info",
    "success",
    "warning",
    "danger",
  ];
  const score = [...seed].reduce(
    (total, char) => total + char.charCodeAt(0),
    0
  );
  return tones[score % tones.length] ?? "primary";
}

function getIcon(iconKey: string) {
  switch (iconKey) {
    case "activity":
      return Activity;
    case "calendar":
    case "timer":
      return Calendar;
    case "chart":
      return Reports;
    case "flash":
      return LeaderboardStar;
    case "globe":
      return DataTransferBoth;
    case "microphone":
      return MessageText;
    case "shield":
      return Shield;
    case "stack":
      return Folder;
    default:
      return WarningCircle;
  }
}

function getSectionIcon(title: string) {
  const normalized = title.toLowerCase();
  if (normalized.includes("writing") || normalized.includes("revision")) {
    return PageEdit;
  }
  if (
    normalized.includes("speaking") ||
    normalized.includes("transcript") ||
    normalized.includes("audio")
  ) {
    return MessageText;
  }
  if (
    normalized.includes("queue") ||
    normalized.includes("ops") ||
    normalized.includes("analytics")
  ) {
    return Reports;
  }
  if (normalized.includes("settings")) {
    return Settings;
  }
  if (normalized.includes("review")) {
    return XrayView;
  }
  if (normalized.includes("dashboard")) {
    return HomeAlt;
  }
  if (normalized.includes("learner")) {
    return UserCircle;
  }
  return Activity;
}

function getHeroStyle(accent: OetVisualAccent): CSSProperties {
  const palette = tonePalette[accent];
  return {
    background: palette.background,
    border: `1px solid ${palette.border}`,
    borderRadius: "28px",
    boxShadow: `0 24px 60px ${palette.glow}`,
    overflow: "hidden",
    position: "relative",
  };
}

function getCardStyle(accent: OetVisualAccent, raised = false): CSSProperties {
  const palette = tonePalette[accent];
  return {
    background: palette.background,
    border: `1px solid ${palette.border}`,
    borderRadius: "24px",
    boxShadow: raised
      ? `0 18px 44px ${palette.glow}`
      : `0 10px 28px ${palette.glow}`,
  };
}

function getPlainCardStyle(accent: OetVisualAccent): CSSProperties {
  const palette = tonePalette[accent];
  return {
    border: `1px solid ${palette.border}`,
    borderRadius: "24px",
    boxShadow: `0 12px 34px ${palette.glow}`,
  };
}

function buildChartOptions(chart: OetVisualChart, accent: OetVisualAccent) {
  const palette = tonePalette[accent];
  return {
    chart: {
      background: "transparent",
      toolbar: { show: false },
      zoom: { enabled: false },
    },
    colors: [
      palette.text,
      "#1fb06f",
      "#1e88e5",
      "#ffb637",
      "#ff5860",
      "#7a6bff",
    ],
    dataLabels: { enabled: false },
    fill: {
      gradient: {
        opacityFrom: 0.38,
        opacityTo: 0.06,
        shadeIntensity: 0.6,
        stops: [0, 95, 100],
        type: "vertical",
      },
      opacity: chart.type === "bar" ? 0.9 : 1,
      type: chart.type === "bar" ? "solid" : "gradient",
    },
    grid: {
      borderColor: "rgba(102, 112, 133, 0.12)",
      strokeDashArray: 5,
    },
    labels: chart.labels ?? chart.categories,
    legend: {
      fontSize: "12px",
      position: "top",
      show: chart.series.length > 1,
    },
    plotOptions: {
      bar: {
        borderRadius: 10,
        columnWidth: "42%",
      },
      pie: {
        donut: {
          labels: {
            show: true,
          },
        },
      },
    },
    stroke: {
      curve: "smooth",
      lineCap: "round",
      width: chart.type === "bar" ? 0 : 3,
    },
    tooltip: {
      theme: "light",
    },
    xaxis: {
      categories: chart.categories,
      labels: {
        style: {
          colors: "#7a7f94",
          fontSize: "12px",
        },
      },
    },
    yaxis: {
      labels: {
        style: {
          colors: "#7a7f94",
          fontSize: "12px",
        },
      },
    },
  };
}

function BoardChart({
  accent,
  chart,
  compact = false,
  title,
}: {
  accent: OetVisualAccent;
  chart: OetVisualChart;
  compact?: boolean;
  title: string;
}) {
  const [Chart, setChart] = useState<any>(null);

  useEffect(() => {
    import("react-apexcharts").then((module) => {
      setChart(() => module.default || module);
    });
  }, []);

  const series =
    chart.type === "donut"
      ? (chart.series[0]?.data ?? [])
      : chart.series.map((item) => ({ data: item.data, name: item.name }));

  return (
    <Card
      className="h-100 border-0 overflow-hidden"
      style={getCardStyle(accent, true)}
    >
      <CardBody className="p-4">
        <div className="d-flex flex-wrap justify-content-between gap-3 mb-3">
          <div>
            <p className="text-secondary mb-1">Signal board</p>
            <h5 className="mb-0">{title}</h5>
          </div>
          <Badge color={tonePalette[accent].badge} className="px-3 py-2">
            {chart.type.toUpperCase()}
          </Badge>
        </div>
        {Chart ? (
          <Chart
            options={buildChartOptions(chart, accent)}
            series={series}
            type={chart.type}
            height={compact ? 220 : 280}
          />
        ) : (
          <div
            className="d-flex align-items-center justify-content-center rounded-4"
            style={{
              background: tonePalette[accent].soft,
              height: compact ? 220 : 280,
            }}
          >
            <span className="text-secondary">Loading chart...</span>
          </div>
        )}
      </CardBody>
    </Card>
  );
}

function ActivityTicker({
  accent,
  items,
  title,
}: {
  accent: OetVisualAccent;
  items: OetVisualActivityItem[];
  title: string;
}) {
  const [Slider, setSlider] = useState<any>(null);

  useEffect(() => {
    import("react-slick").then((module) => {
      setSlider(() => module.default || module);
    });
  }, []);

  const settings = {
    arrows: false,
    autoplay: true,
    autoplaySpeed: 2600,
    dots: false,
    focusOnSelect: true,
    slidesToShow: Math.min(items.length, 3),
    speed: 900,
    vertical: true,
    verticalSwiping: true,
  };

  const content = items.map((item) => {
    const palette = tonePalette[item.tone];
    return (
      <div key={item.badge + item.title} className="px-1">
        <Card
          className="border-0 mb-3 overflow-hidden"
          style={{
            background: palette.background,
            borderRadius: "20px",
            boxShadow: `0 10px 26px ${palette.glow}`,
          }}
        >
          <CardBody>
            <div className="d-flex justify-content-between gap-2 mb-2">
              <Badge color={palette.badge}>{item.badge}</Badge>
              <span className="text-secondary f-s-12">{item.meta}</span>
            </div>
            <h6 className="mb-1">{item.title}</h6>
            <p className="mb-0 text-secondary f-s-13">{item.description}</p>
          </CardBody>
        </Card>
      </div>
    );
  });

  return (
    <Card
      className="h-100 border-0 overflow-hidden"
      style={getCardStyle(accent)}
    >
      <CardBody className="p-4">
        <div className="d-flex justify-content-between align-items-center mb-3">
          <div>
            <p className="text-secondary mb-1">Live feed</p>
            <h5 className="mb-0">{title}</h5>
          </div>
          <Badge color={tonePalette[accent].badge} className="px-3 py-2">
            Active
          </Badge>
        </div>
        {Slider ? (
          <Slider {...settings}>{content}</Slider>
        ) : (
          <div className="vstack gap-3">{content.slice(0, 3)}</div>
        )}
      </CardBody>
    </Card>
  );
}

function AvatarStack({
  accent,
  avatars,
}: {
  accent: OetVisualAccent;
  avatars: Array<{ avatarUrl: string; name: string; tone: OetVisualAccent }>;
}) {
  return (
    <ul className="avatar-group justify-content-start mb-0">
      {avatars.map((avatar) => (
        <li
          key={avatar.name}
          className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden bg-${avatar.tone}-300 b-2-light`}
          title={avatar.name}
        >
          <img alt={avatar.name} className="img-fluid" src={avatar.avatarUrl} />
        </li>
      ))}
      <li
        className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden text-white`}
        style={{
          background: tonePalette[accent].text,
        }}
      >
        +{Math.max(avatars.length, 2)}
      </li>
    </ul>
  );
}

function HeroBanner({
  action,
  description,
  icon: Icon,
  mainTitle,
  summary,
  visual,
}: {
  action: PageAction | undefined;
  description: string | undefined;
  icon: React.ElementType;
  mainTitle: string;
  summary: string;
  visual: ReturnType<typeof getOetVisualConfig>;
}) {
  const accent = visual.accent;
  const palette = tonePalette[accent];
  const AccentIcon = getIcon(visual.heroStats[0]?.icon ?? "activity");

  return (
    <Card
      className="border-0 overflow-hidden mb-4"
      style={getHeroStyle(accent)}
    >
      <CardBody className="p-4 p-xl-5 position-relative">
        <div
          className="position-absolute end-0 top-0 translate-middle-y opacity-25 d-none d-xl-block"
          style={{
            color: palette.text,
          }}
        >
          <AccentIcon height={140} width={140} />
        </div>
        <Row className="align-items-center g-4">
          <Col xl={7}>
            <div className="d-flex align-items-center gap-3 mb-3">
              <span
                className="d-flex align-items-center justify-content-center rounded-4"
                style={{
                  background: palette.soft,
                  color: palette.text,
                  height: 64,
                  width: 64,
                }}
              >
                <Icon height={30} width={30} />
              </span>
              <div>
                <p className="text-secondary mb-1 text-uppercase f-s-12">
                  {visual.recipe} board
                </p>
                <h2 className="mb-0">{mainTitle}</h2>
              </div>
            </div>
            <p className="text-secondary mb-3">{description ?? summary}</p>
            <div className="d-flex flex-wrap gap-2 mb-3">
              {visual.chips.map((chip) => (
                <Badge
                  color={palette.badge}
                  className="px-3 py-2 animate__animated animate__fadeIn"
                  key={chip}
                >
                  {chip}
                </Badge>
              ))}
            </div>
            <div className="d-flex flex-wrap align-items-center gap-3">
              <AvatarStack accent={accent} avatars={visual.avatars} />
              <p className="mb-0 text-secondary f-s-13">{summary}</p>
            </div>
          </Col>
          <Col xl={5}>
            <Card
              className="border-0 h-100 overflow-hidden"
              style={{
                background: "rgba(255, 255, 255, 0.72)",
                backdropFilter: "blur(12px)",
                borderRadius: "24px",
                boxShadow: `0 16px 40px ${palette.glow}`,
              }}
            >
              <CardBody className="p-4">
                <div className="d-flex justify-content-between align-items-center mb-3">
                  <div>
                    <p className="text-secondary mb-1">
                      Why this board matters
                    </p>
                    <h5 className="mb-0">Visual pulse</h5>
                  </div>
                  <Badge color={palette.badge}>Live</Badge>
                </div>
                <div className="vstack gap-3">
                  {visual.activityItems.slice(0, 2).map((item) => (
                    <div
                      key={item.badge + item.title}
                      className="d-flex gap-3 p-3 rounded-4"
                      style={{
                        background: tonePalette[item.tone].soft,
                      }}
                    >
                      <span
                        className="d-flex align-items-center justify-content-center rounded-circle flex-shrink-0"
                        style={{
                          background: tonePalette[item.tone].glow,
                          color: tonePalette[item.tone].text,
                          height: 44,
                          width: 44,
                        }}
                      >
                        <AccentIcon height={18} width={18} />
                      </span>
                      <div>
                        <p className="mb-1 f-s-12 text-secondary">
                          {item.meta}
                        </p>
                        <h6 className="mb-1">{item.title}</h6>
                        <p className="mb-0 text-secondary f-s-13">
                          {item.description}
                        </p>
                      </div>
                    </div>
                  ))}
                </div>
                {action ? (
                  <Button
                    tag={Link}
                    href={action.href}
                    color={accent}
                    className="w-100 mt-4"
                  >
                    {action.label}
                  </Button>
                ) : null}
              </CardBody>
            </Card>
          </Col>
        </Row>
      </CardBody>
    </Card>
  );
}

export const OetPageShell = ({
  action,
  children,
  description,
  icon,
  layoutMode = "full",
  mainTitle,
  path,
  title,
}: OetPageShellProps) => {
  const visual =
    layoutMode === "full"
      ? getOetVisualConfig({
          mainTitle,
          path,
          title,
        })
      : null;

  return (
    <Container fluid className="mt-3">
      <Row className="align-items-center">
        <Col xs={12}>
          <Breadcrumbs
            Icon={icon}
            mainTitle={mainTitle}
            path={path}
            title={title}
          />
        </Col>
      </Row>
      {visual ? (
        <>
          <HeroBanner
            action={action}
            description={description}
            icon={icon}
            mainTitle={mainTitle}
            summary={visual.summary}
            visual={visual}
          />
          <OetMetricGrid items={visual.heroStats} />
          <Row className="g-4 mb-4">
            <Col xl={visual.recipe === "workflow" ? 7 : 8}>
              <BoardChart
                accent={visual.accent}
                chart={visual.chart}
                compact={visual.recipe === "workflow"}
                title={`${mainTitle} signal board`}
              />
            </Col>
            <Col xl={visual.recipe === "workflow" ? 5 : 4}>
              <ActivityTicker
                accent={visual.accent}
                items={visual.activityItems}
                title={`${mainTitle} activity`}
              />
            </Col>
          </Row>
        </>
      ) : null}
      {children}
    </Container>
  );
};

export const OetMetricGrid = ({ items }: { items: MetricItem[] }) => {
  return (
    <Row className="g-4 mb-4">
      {items.map((item, index) => {
        const tone = item.tone ?? getTone(item.label + index);
        const palette = tonePalette[tone];
        const Icon = getIcon(item.icon ?? "activity");

        return (
          <Col md={6} xl={3} key={item.label}>
            <Card
              className="h-100 border-0 overflow-hidden animate__animated animate__fadeInUp"
              style={getCardStyle(tone)}
            >
              <CardBody className="position-relative p-4">
                <div className="d-flex justify-content-between align-items-start gap-3">
                  <div>
                    <p className="text-secondary mb-2">{item.label}</p>
                    <h3 className="mb-2">{item.value}</h3>
                    {item.helper ? (
                      <p className="text-secondary f-s-13 mb-0">
                        {item.helper}
                      </p>
                    ) : null}
                  </div>
                  <span
                    className="rounded-4 d-flex align-items-center justify-content-center flex-shrink-0"
                    style={{
                      background: palette.soft,
                      color: palette.text,
                      height: 52,
                      width: 52,
                    }}
                  >
                    <Icon height={22} width={22} />
                  </span>
                </div>
                <div className="d-flex justify-content-between align-items-center mt-4">
                  <Badge color={palette.badge}>{item.delta ?? "Live"}</Badge>
                  <div className="d-flex align-items-end gap-1">
                    {[16, 22, 18, 28, 24].map((bar, barIndex) => (
                      <span
                        key={bar + item.label + barIndex}
                        className="rounded-top"
                        style={{
                          background:
                            barIndex === 4
                              ? palette.text
                              : "rgba(122, 127, 148, 0.24)",
                          height: `${bar}px`,
                          width: "6px",
                        }}
                      />
                    ))}
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
        );
      })}
    </Row>
  );
};

export const OetSectionCard = ({
  children,
  description,
  title,
}: SectionCardProps) => {
  const tone = getTone(title);
  const palette = tonePalette[tone];
  const Icon = getSectionIcon(title);

  return (
    <Card
      className="h-100 border-0 overflow-hidden"
      style={getPlainCardStyle(tone)}
    >
      <CardHeader
        className="border-0"
        style={{
          background: palette.soft,
          paddingBottom: description ? "1rem" : "1.35rem",
          paddingTop: "1.35rem",
        }}
      >
        <div className="d-flex justify-content-between gap-3">
          <div className="d-flex gap-3">
            <span
              className="d-flex align-items-center justify-content-center rounded-4 flex-shrink-0"
              style={{
                background: palette.glow,
                color: palette.text,
                height: 48,
                width: 48,
              }}
            >
              <Icon height={20} width={20} />
            </span>
            <div>
              <h5 className="mb-1">{title}</h5>
              {description ? (
                <p className="mb-0 text-secondary">{description}</p>
              ) : null}
            </div>
          </div>
          <Badge color={palette.badge} className="align-self-start">
            Live
          </Badge>
        </div>
      </CardHeader>
      <CardBody className="p-4">{children}</CardBody>
    </Card>
  );
};

export const ScoreRangeBadge = ({ value }: { value: string }) => (
  <Badge color="light-primary" className="px-3 py-2 rounded-pill">
    Score Range {value}
  </Badge>
);

export const ConfidenceBadge = ({
  value,
}: {
  value: "low" | "moderate" | "high";
}) => {
  const color =
    value === "high" ? "success" : value === "moderate" ? "warning" : "danger";
  return (
    <Badge
      color={`light-${color}`}
      className="px-3 py-2 text-capitalize rounded-pill"
    >
      Confidence {value}
    </Badge>
  );
};

export const AsyncStatusBadge = ({
  status,
}: {
  status: AsyncWorkflowStatus;
}) => {
  const meta = getAsyncWorkflowMeta(status);
  return <Badge color={meta.badgeClass}>{meta.label}</Badge>;
};

export const AsyncStateAlert = ({
  status,
  title,
}: {
  status: AsyncWorkflowStatus;
  title: string;
}) => {
  const meta = getAsyncWorkflowMeta(status);
  const tone = getTone(meta.badgeClass);
  const palette = tonePalette[tone];

  return (
    <Card className="border-0 overflow-hidden mb-4" style={getCardStyle(tone)}>
      <CardBody className="d-flex flex-wrap align-items-center justify-content-between gap-3 p-4">
        <div className="d-flex align-items-start gap-3">
          <span
            className="d-flex align-items-center justify-content-center rounded-4"
            style={{
              background: palette.soft,
              color: palette.text,
              height: 52,
              width: 52,
            }}
          >
            <WarningCircle height={22} width={22} />
          </span>
          <div>
            <strong>{title}</strong>
            <p className="mb-0 text-secondary">{meta.description}</p>
          </div>
        </div>
        <AsyncStatusBadge status={status} />
      </CardBody>
    </Card>
  );
};

export const RecommendedActionStrip = ({
  href,
  label,
  summary,
  title,
}: {
  href: string;
  label: string;
  summary: string;
  title: string;
}) => {
  return (
    <Card
      className="mb-4 border-0 overflow-hidden"
      style={getCardStyle("primary", true)}
    >
      <CardBody className="d-flex flex-wrap align-items-center justify-content-between gap-3 p-4">
        <div className="d-flex gap-3 align-items-start">
          <span
            className="d-flex align-items-center justify-content-center rounded-4"
            style={{
              background: tonePalette.primary.soft,
              color: tonePalette.primary.text,
              height: 56,
              width: 56,
            }}
          >
            <PageEdit height={24} width={24} />
          </span>
          <div>
            <p className="text-secondary mb-1">Next recommended action</p>
            <h5 className="mb-1">{title}</h5>
            <p className="mb-0 text-secondary">{summary}</p>
          </div>
        </div>
        <Button tag={Link} href={href} color="primary">
          {label}
        </Button>
      </CardBody>
    </Card>
  );
};

export const StickyActionBar = ({ actions }: { actions: PageAction[] }) => {
  return (
    <div className="position-sticky bottom-0 mt-4" style={{ zIndex: 12 }}>
      <Card
        className="border-0 overflow-hidden"
        style={{
          backdropFilter: "blur(16px)",
          background: "rgba(255, 255, 255, 0.88)",
          borderRadius: "22px",
          boxShadow: "0 18px 44px rgba(38, 45, 77, 0.14)",
        }}
      >
        <CardBody className="p-3">
          <div className="d-flex flex-wrap gap-2 justify-content-end">
            {actions.map((action, index) => (
              <Button
                key={action.href + action.label}
                tag={Link}
                href={action.href}
                color={index === 0 ? "primary" : "light-secondary"}
              >
                {action.label}
              </Button>
            ))}
          </div>
        </CardBody>
      </Card>
    </div>
  );
};

export const EmptyStateCard = ({
  ctaHref,
  ctaLabel,
  summary,
  title,
}: {
  ctaHref: string;
  ctaLabel: string;
  summary: string;
  title: string;
}) => {
  return (
    <Card
      className="border-0 overflow-hidden"
      style={getCardStyle("info", true)}
    >
      <CardBody className="text-center py-5 px-4">
        <div className="d-flex justify-content-center mb-3">
          <span
            className="d-flex align-items-center justify-content-center rounded-4"
            style={{
              background: tonePalette.info.soft,
              color: tonePalette.info.text,
              height: 64,
              width: 64,
            }}
          >
            <WarningCircle height={28} width={28} />
          </span>
        </div>
        <h5 className="mb-2">{title}</h5>
        <p className="text-secondary mb-3">{summary}</p>
        <Button tag={Link} href={ctaHref} color="primary">
          {ctaLabel}
        </Button>
      </CardBody>
    </Card>
  );
};

export const WaveformBars = ({
  bars,
  currentIndex,
}: {
  bars: number[];
  currentIndex?: number;
}) => {
  return (
    <div
      className="d-flex align-items-end gap-1 h-100"
      aria-label="Waveform preview"
      style={{
        minHeight: "86px",
      }}
    >
      {bars.map((bar, index) => (
        <span
          key={`${bar}-${index}`}
          className="d-inline-block rounded-top animate__animated animate__fadeInUp"
          style={{
            background:
              currentIndex === index
                ? tonePalette.primary.text
                : "rgba(75, 62, 246, 0.24)",
            height: `${Math.max(bar, 12)}px`,
            width: "9px",
          }}
        />
      ))}
    </div>
  );
};

export const KeyValueList = ({
  items,
}: {
  items: Array<{ label: string; value: ReactNode }>;
}) => {
  return (
    <ul className="list-unstyled mb-0 d-flex flex-column gap-2">
      {items.map((item) => (
        <li
          className="d-flex justify-content-between align-items-start gap-3 p-3 rounded-4"
          key={item.label}
          style={{
            background: "#f8f9fc",
            border: "1px solid rgba(102, 112, 133, 0.08)",
          }}
        >
          <span className="text-secondary">{item.label}</span>
          <span className="text-end fw-semibold">{item.value}</span>
        </li>
      ))}
    </ul>
  );
};

export const OetChartPanel = BoardChart;
export const OetActivityTicker = ActivityTicker;
