import React from "react";
import { Col, Container, Row } from "reactstrap";
import Link from "next/link";
import { IconChevronRight } from "@tabler/icons-react";

type ElementItem = {
  title: string;
  description: string;
  href: string;
  colorClass: string;
};

const elements: ElementItem[] = [
  {
    title: "Buttons",
    description:
      "Apply Custom button styles to forms, dialogs, and various elements, featuring support for multiple sizes and states.",
    href: "/ui-kit/buttons",
    colorClass: "icons-primary",
  },
  {
    title: "Cards",
    description:
      "Create stylish cards with Admin Element for organized content presentation and a sleek user interface.",
    href: "/ui-kit/cards",
    colorClass: "icons-secondary",
  },
  {
    title: "List",
    description:
      "Easily design lists with Admin Element intuitive elements for structured content display, enhancing user experience.",
    href: "/ui-kit/lists",
    colorClass: "icons-dark",
  },
  {
    title: "Alerts",
    description:
      "Create alerts using Admin Elements intuitive elements to effectively communicate messages.",
    href: "/ui-kit/alert",
    colorClass: "icons-secondary",
  },
  {
    title: "Accordions",
    description:
      "Create collapsible accordion in Admin for organized content presentation, optimizing user interaction.",
    href: "/ui-kit/accordions",
    colorClass: "icons-dark",
  },
  {
    title: "Dropdown",
    description:
      "Create dropdown using Admin Elements for enhanced user interaction and intuitive navigation.",
    href: "/ui-kit/dropdown",
    colorClass: "icons-primary",
  },
  {
    title: "Dividers",
    description:
      "Create dividers effortlessly with Admin Elements utilities, enhancing content organization.",
    href: "/ui-kit/divider",
    colorClass: "icons-dark",
  },
  {
    title: "Progress",
    description:
      "Utilize Progress components for visually appealing representation of task completion and data loading.",
    href: "/ui-kit/progress",
    colorClass: "icons-primary",
  },
  {
    title: "Notifications",
    description:
      "Employ Admin notification Elements for streamlined user alerts, enhancing interactivity and user experience.",
    href: "/ui-kit/notifications",
    colorClass: "icons-secondary",
  },
];

const ElementSection = () => {
  return (
    <Container>
      <Row>
        <Col xl={{ size: 6, offset: 3 }}>
          <div className="landing-title text-md-center">
            <h2>
              <span className="highlight-title">Elements</span> of PolytronX
            </h2>
            <p>
              offer a responsive and user-friendly interface, streamlining the
              design and development of web applications by providing a robust
              set of pre-built components and reusable interface patterns.
            </p>
          </div>
        </Col>
      </Row>
      <Row className="gy-3">
        {elements.map((el, index) => (
          <Col sm="6" lg="4" key={index}>
            <div className="element-card">
              <div>
                <div className="element-content">
                  <h4>{el.title}</h4>
                  <p>{el.description}</p>
                  <Link
                    className="link-btn link-primary mt-3 d-inline-flex align-items-center gap-1"
                    href={el.href}
                    target={"_blank"}
                  >
                    View {el.title}
                    <IconChevronRight size={18} />
                  </Link>
                </div>
              </div>
            </div>
          </Col>
        ))}
      </Row>
    </Container>
  );
};

export default ElementSection;
