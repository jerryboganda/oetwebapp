"use client";
import React from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "prismjs/themes/prism.css";
import {
  Card,
  CardHeader,
  CardBody,
  Col,
  UncontrolledCollapse,
  Button,
  Badge,
  Container,
  Row,
} from "reactstrap";
import {
  IconShoppingCart,
  IconLineDashed,
  IconSpeakerphone,
  IconMail,
  IconMoonFilled,
  IconCode,
  IconBriefcase,
} from "@tabler/icons-react";
import {
  badgeColors,
  badgePositionData,
  headingData,
  lightBadgeColors,
  outlineBadgeColors,
  radiusBadgeData,
} from "@/Data/UiKit/BadgeData/BadgePageData";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

let DOMPurify: any = null;
if (typeof window !== "undefined") {
  DOMPurify = require("dompurify");
}

export interface BadgeIconItem {
  icon: React.ReactNode;
  color: string;
}

export const badgeIconData: BadgeIconItem[] = [
  { icon: <IconShoppingCart size={22} />, color: "primary" },
  { icon: <IconLineDashed size={22} />, color: "secondary" },
  { icon: <IconSpeakerphone size={22} />, color: "success" },
  { icon: <IconMail size={22} />, color: "danger" },
  { icon: <IconMoonFilled size={22} />, color: "dark" },
];

const badgeWithButtonData = [
  {
    label: "Notifications",
    color: "light-primary",
    badge: "4",
    badgeColor: "primary",
  },
  {
    label: "Disable",
    color: "light-secondary",
    badgeHtml: `<span class="position-absolute top-0 start-100 translate-middle p-2 bg-secondary border border-light rounded-circle">
  <span class="visually-hidden">Disable</span>
</span>`,
  },
  {
    label: "Inbox",
    color: "light-success",
    badge: "99+",
    badgeColor: "success",
  },
  {
    label: "Unread",
    color: "light-danger",
    icon: "ti ti-bell-ringing",
    badgeColor: "danger",
  },
  {
    label: "Inbox",
    color: "outline-warning",
    badge: "99+",
    badgeColor: "warning",
  },
  {
    label: "50% Off",
    color: "light-info",
    badge: "New",
    badgeColor: "info",
    extraClass: "f-s-14",
  },
  {
    label: "1 missed call",
    color: "light-dark",
    badgeHtml: `<span class="position-absolute top-0 start-100 translate-middle p-2 bg-dark border border-light rounded-circle animate__animated animate__fadeIn animate__infinite animate__slower">
  <span class="visually-hidden">Busy</span>
</span>`,
  },
];

const BadgesPage = () => {
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Badges"
          title="Ui Kits"
          path={["Badges"]}
          Icon={IconBriefcase}
        />
        <PrismCodeWrapper>
          <Row>
            <Col sm="12" xl="6">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5 className="mb-0">Basic Badges</h5>
                  <a id="togglerBadge">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {badgeColors.map((color) => (
                      <Badge key={color} color={color}>
                        {color.charAt(0).toUpperCase() + color.slice(1)}
                      </Badge>
                    ))}
                  </div>

                  <UncontrolledCollapse toggler="#togglerBadge">
                    <pre className="basicbadges mt-3">
                      <code className="language-html">
                        {`<div class="d-flex gap-2 flex-wrap">
  <Badge class="badge text-bg-primary">Primary</Badge>
  <Badge class="badge text-bg-secondary">Secondary</Badge>
  <Badge class="badge text-bg-success">Success</Badge>
  <Badge class="badge text-bg-danger">Danger</Badge>
  <Badge class="badge text-bg-warning">Warning</Badge>
  <Badge class="badge text-bg-info">Info</Badge>
  <Badge class="badge text-bg-light">Light</Badge>
  <Badge class="badge text-bg-dark">Dark</Badge>
</div>`}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm="12" xl="6">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5 className="mb-0">Outline Badges</h5>
                  <a id="togglerOutlineBadge">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {outlineBadgeColors.map((color) => (
                      <span
                        key={color}
                        className={`badge text-outline-${color}`}
                      >
                        {color.charAt(0).toUpperCase() + color.slice(1)}
                      </span>
                    ))}
                  </div>

                  <UncontrolledCollapse toggler="#togglerOutlineBadge">
                    <pre className="outlinebadge mt-3" id="outlinebadgeexample">
                      <code className="language-html">
                        {outlineBadgeColors
                          .map(
                            (color) =>
                              `<Badge class="text-outline-${color}">${color.charAt(0).toUpperCase() + color.slice(1)}</Badge>`
                          )
                          .join("\n")}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm="12" xl="6">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5 className="mb-0">Light Badges</h5>
                  <a id="togglerLightBadge">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {lightBadgeColors.map(({ color, icon }) => (
                      <span key={color} className={`badge text-light-${color}`}>
                        {icon && <i className={`ti ${icon} me-1`}></i>}
                        {color.charAt(0).toUpperCase() + color.slice(1)}
                      </span>
                    ))}
                  </div>

                  <UncontrolledCollapse toggler="#togglerLightBadge">
                    <pre>
                      <code className="language-html">
                        {lightBadgeColors
                          .map(
                            ({ color, icon }) =>
                              `<Badge class="badge text-light-${color}">${
                                icon ? `<i class="ti ${icon} me-1"></i>` : ""
                              }${color.charAt(0).toUpperCase() + color.slice(1)}</Badge>`
                          )
                          .join("\n")}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm="12" xl="6">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5 className="mb-0">Radius Option on Badges</h5>
                  <a id="togglerRadius">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-2 flex-wrap">
                    {radiusBadgeData.map(({ color, radius }) => (
                      <span
                        key={color}
                        className={`badge text-bg-${color} b-r-${radius}`}
                      >
                        {color.charAt(0).toUpperCase() + color.slice(1)}
                      </span>
                    ))}
                  </div>

                  <UncontrolledCollapse toggler="#togglerRadius">
                    <pre className="radiusbadge mt-3" id="radiusbadgeExample">
                      <code className="language-html">
                        {radiusBadgeData
                          .map(
                            ({ color, radius }) =>
                              `<Badge class="badge text-bg-${color} b-r-${radius}">${color.charAt(0).toUpperCase() + color.slice(1)}</Badge>`
                          )
                          .join("\n")}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm="12" xl="6">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5 className="mb-0">Badges Position</h5>
                  <a id="togglerPosition">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-3 flex-wrap">
                    {badgePositionData.map(
                      ({ label, bgColor, positionClass }) => (
                        <Button
                          key={label}
                          color={`outline-${bgColor}`}
                          className="position-relative"
                        >
                          {label}
                          <span
                            className={`position-absolute ${positionClass} translate-middle p-1 bg-${bgColor} border border-light rounded-circle`}
                          >
                            <span className="visually-hidden">{label}</span>
                          </span>
                        </Button>
                      )
                    )}
                  </div>

                  <UncontrolledCollapse toggler="#togglerPosition">
                    <pre>
                      <code className="language-html">
                        {badgePositionData
                          .map(
                            ({ label, bgColor, positionClass }) =>
                              `<button type="button" class="btn btn-outline-${bgColor} position-relative">
                        ${label}
                        <span class="position-absolute ${positionClass} translate-middle p-1 bg-${bgColor} border border-light rounded-circle">
                          <span class="visually-hidden">${label}</span>
                        </span>
                      </button>`
                          )
                          .join("\n")}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={12} xl={6}>
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5>Icon&#39;s badges</h5>
                  <a href="#" id="toggleIconBadge">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-3 flex-wrap">
                    {badgeIconData.map(({ icon, color }, index) => (
                      <a
                        key={index}
                        href="#"
                        className={`position-relative bg-light-${color} px-2 py-1 rounded`}
                      >
                        {icon}
                        <span
                          className={`position-absolute top-0 start-100 translate-middle p-1 bg-${color} rounded-circle animate__animated animate__fadeIn animate__infinite animate__fast`}
                        />
                      </a>
                    ))}
                  </div>
                </CardBody>

                <UncontrolledCollapse toggler="#toggleIconBadge">
                  <pre className="mt-3">
                    <code className="language-jsx">
                      {`<a className="position-relative bg-light-primary px-2 py-1 rounded" href="#">
  <IconShoppingCart size={22} />
  <span className="position-absolute top-0 start-100 translate-middle p-1 bg-primary rounded-circle animate__animated animate__fadeIn animate__infinite animate__fast" />
</a>`}
                    </code>
                  </pre>
                </UncontrolledCollapse>
              </Card>
            </Col>
            <Col xl="12">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5>Badges with button</h5>
                  <a id="togglerOutline">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="d-flex gap-4 flex-wrap">
                    {badgeWithButtonData.map(
                      (
                        {
                          label,
                          color,
                          badge,
                          badgeColor,
                          icon,
                          badgeHtml,
                          extraClass,
                        },
                        idx
                      ) => (
                        <Button
                          key={idx}
                          color={color}
                          className={`position-relative ${extraClass || ""}`}
                        >
                          {label}
                          {badgeHtml ? (
                            <span
                              dangerouslySetInnerHTML={{
                                __html:
                                  typeof window !== "undefined" && DOMPurify
                                    ? DOMPurify.sanitize(badgeHtml)
                                    : badgeHtml,
                              }}
                            />
                          ) : (
                            <span
                              className={`position-absolute top-0 start-100 translate-middle badge rounded-pill bg-${badgeColor} badge-notification`}
                            >
                              {icon ? <i className={icon} /> : badge}
                            </span>
                          )}
                        </Button>
                      )
                    )}
                  </div>
                </CardBody>

                {/* Collapsible HTML source preview */}
                <UncontrolledCollapse toggler="#togglerOutline">
                  <pre className="mt-3">
                    <code className="language-html">
                      {badgeWithButtonData
                        .map(
                          ({
                            label,
                            color,
                            badge,
                            badgeColor,
                            icon,
                            badgeHtml,
                            extraClass,
                          }) => {
                            const btnClass = `btn btn-${color} position-relative${
                              extraClass ? " " + extraClass : ""
                            }`;

                            if (badgeHtml) {
                              return `<button type="button" class="${btnClass}">
  ${label}
  ${badgeHtml}
</button>`;
                            }

                            return `<button type="button" class="${btnClass}">
  ${label}
  <span class="position-absolute top-0 start-100 translate-middle badge rounded-pill bg-${badgeColor} badge-notification">
    ${icon ? `<i class="${icon}"></i>` : badge}
  </span>
</button>`;
                          }
                        )
                        .join("\n\n")}
                    </code>
                  </pre>
                </UncontrolledCollapse>
              </Card>
            </Col>
            <Col xl="12">
              <Card>
                <CardHeader className="d-flex justify-content-between code-header">
                  <h5>Radius Badges</h5>
                  <a id="togglerRadius1">
                    <IconCode className="source cursor-pointer" size={32} />
                  </a>
                </CardHeader>

                <CardBody>
                  <div className="">
                    {headingData.map(({ level, label }, idx) => {
                      const Heading = React.createElement(
                        level,
                        { key: idx, className: "mb-3" },
                        `${label} `
                      );
                      return Heading;
                    })}
                  </div>
                </CardBody>

                {/* Collapsible HTML source preview */}
                <UncontrolledCollapse toggler="#togglerRadius1">
                  <pre className="mt-3">
                    <code className="language-html">
                      {headingData
                        .map(({ level, label }) => {
                          return `<${level} class="mb-3">${label} </${level}>`;
                        })
                        .join("\n")}
                    </code>
                  </pre>
                </UncontrolledCollapse>
              </Card>
            </Col>
          </Row>
        </PrismCodeWrapper>
      </Container>
    </div>
  );
};

export default BadgesPage;
