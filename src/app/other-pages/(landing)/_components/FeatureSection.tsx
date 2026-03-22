import React from "react";
import { Container, Row, Col, Badge } from "reactstrap";
import {
  IconBrandBootstrap,
  IconCircleLetterW,
  IconBrandSass,
  IconChartPie,
  IconPackage,
  IconMap2,
  IconChartRadar,
  IconBrandGoogle,
  IconBriefcase,
  IconApps,
  IconBrandNpm,
  IconCalendarPlus,
} from "@tabler/icons-react";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";

const features = [
  {
    title: "Bootstrap",
    icon: <IconBrandBootstrap size={36} className="text-primary" />,
    badgeColor: "info",
    badgeText: "Framework",
  },
  {
    title: "W3C",
    icon: <IconCircleLetterW size={36} className="text-primary" />,
    badgeColor: "success",
    badgeText: "Validation",
  },
  {
    title: "SASS",
    icon: <IconBrandSass size={36} className="text-primary" />,
    badgeColor: "danger",
    badgeText: "Professional grade",
  },
  {
    title: "Apex Chart",
    icon: <IconChartPie size={36} className="text-primary" />,
    badgeColor: "secondary",
    badgeText: "Charts",
  },
  {
    title: "Webpack",
    icon: <IconPackage size={36} className="text-primary" />,
    badgeColor: "primary",
    badgeText: "Module bundler",
  },
  {
    title: "Google Map",
    icon: <IconMap2 size={36} className="text-primary" />,
    badgeColor: "warning",
    badgeText: "Maps",
  },
  {
    title: "Chart js",
    icon: <IconChartRadar size={36} className="text-primary" />,
    badgeColor: "warning",
    badgeText: "Charts",
  },
  {
    title: "Google Fonts",
    icon: <IconBrandGoogle size={36} className="text-primary" />,
    badgeColor: "dark",
    badgeText: "Fonts",
  },
  {
    title: "UI kits",
    icon: <IconBriefcase size={36} className="text-primary" />,
    badgeColor: "primary",
    badgeText: "Components",
  },
  {
    title: "Apps",
    icon: <IconApps size={36} className="text-primary" />,
    badgeColor: "success",
    badgeText: "Apps",
  },
  {
    title: "NPM",
    icon: <IconBrandNpm size={36} className="text-primary" />,
    badgeColor: "danger",
    badgeText: "Pkg Manager",
  },
  {
    title: "Fullcalendar",
    icon: <IconCalendarPlus size={36} className="text-primary" />,
    badgeColor: "info",
    badgeText: "Event Calendar",
  },
];
const FeaturesSection = () => {
  return (
    <section className="features-section" id="Features">
      <Container>
        <SectionHeading
          title="Core"
          highlight="features"
          description="Admin features, developers can easily customize the appearance
                and behavior of their applications, ensuring a consistent and
                visually appealing experience across different devices and
                screen sizes."
        />

        <Row className="features-list">
          <Col>
            <ul className="row list-unstyled">
              {features.map((feature, index) => (
                <li className="col-6 col-md-3 col-xl-2" key={index}>
                  <div className="features-icon text-center">
                    {feature.icon}
                    <div className="features-content mt-2">
                      <h5>{feature.title}</h5>
                      <Badge
                        color={`light-${feature.badgeColor}`}
                        className="mt-1"
                      >
                        {feature.badgeText}
                      </Badge>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          </Col>
        </Row>
      </Container>
    </section>
  );
};

export default FeaturesSection;
