"use client";
import React, { useState } from "react";
import "prismjs/themes/prism.css";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Collapse,
  Container,
  Row,
  UncontrolledCollapse,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  IconArrowBigRightLineFilled,
  IconBriefcase,
  IconCode,
  IconCornerDownRightDouble,
  IconNorthStar,
} from "@tabler/icons-react";
import PrismCodeWrapper from "@/Component/CommonElements/PrismCodeWrapper";

const BulletPage = () => {
  type BulletTypes =
    | "diamond"
    | "heart"
    | "burst"
    | "star"
    | "rightArrow"
    | "line"
    | "arrow"
    | "cornerArrow"
    | "circle"
    | "triangle"
    | "square"
    | "plus";

  const [isOpen, setIsOpen] = useState<Record<BulletTypes, boolean>>({
    diamond: false,
    heart: false,
    burst: false,
    star: false,
    rightArrow: false,
    line: false,
    arrow: false,
    cornerArrow: false,
    circle: false,
    triangle: false,
    square: false,
    plus: false,
  });

  const toggleCollapse = (type: BulletTypes) => {
    setIsOpen((prev) => ({ ...prev, [type]: !prev[type] }));
  };

  const bulletVariants = [
    "primary",
    "secondary",
    "success",
    "danger",
    "warning",
    "info",
    "light",
    "dark",
  ];

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Bullet"
          title="Ui Kits"
          path={["Bullet"]}
          Icon={IconBriefcase}
        />
        <PrismCodeWrapper>
          <Row className="list-item bullet-item">
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Diamond Bullet</h5>
                  <a href="#" id="togglerBullet1">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`diamond diamond-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet1">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="diamond diamond-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Heart Bullet</h5>
                  <a href="#" id="togglerBullet2">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`heart heart-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet2">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="heart heart-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
                `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Burst Bullet</h5>
                  <a href="#" id="togglerBullet3">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`burst burst-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet3">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="burst burst-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
                  `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Star Bullet</h5>
                  <a href="#" id="togglerBullet4">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li key={variant} className="bullet-list-box">
                        <IconNorthStar
                          size={16}
                          className={`text-${variant} me-2`}
                        />
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet4">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li><IconNorthStar size={20} className="text-${variant} me-2" /> ${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
                  `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Right Arrow Bullet</h5>
                  <a
                    onClick={() => toggleCollapse("rightArrow")}
                    className="cursor-pointer"
                  >
                    <IconCode
                      data-source="rightarrowbullet"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li key={variant} className="bullet-list-box right-arrow">
                        <IconArrowBigRightLineFilled
                          size={22}
                          className={`text-${variant} me-2`}
                        />
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <Collapse isOpen={isOpen.rightArrow}>
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="right-arrow"><IconArrowBigRightLineFilled size={22} className="text-${variant} me-2" /> ${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
                  `}
                      </code>
                    </pre>
                  </Collapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Line Bullet</h5>
                  <a
                    onClick={() => toggleCollapse("line")}
                    className="cursor-pointer"
                  >
                    <IconCode
                      data-source="linebullet"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`line line-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <Collapse isOpen={isOpen.line}>
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="line line-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </Collapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Arrow Bullet</h5>
                  <a
                    onClick={() => toggleCollapse("arrow")}
                    className="cursor-pointer"
                  >
                    <IconCode
                      data-source="arrowbullet"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`arrow1 arrow-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <Collapse isOpen={isOpen.arrow}>
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="arrow arrow-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </Collapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Corner Arrow Bullet</h5>
                  <a href="#" id="togglerBullet5">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`bullet-list-box corner-arrow corner-arrow-bullet-${variant}`}
                      >
                        <IconCornerDownRightDouble
                          size={22}
                          className={`text-${variant} me-2`}
                        />
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet5">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="corner-arrow corner-arrow-bullet-${variant}"><IconCornerDownRightDouble size={22} className="text-${variant} me-2" /> ${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Circle Bullet</h5>
                  <a href="#" id="togglerBullet6">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`circle circle-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet6">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="circle circle-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Triangle Bullet</h5>
                  <a href="#" id="togglerBullet7">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`triangle triangle-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet7">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="triangle triangle-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Square Bullet</h5>
                  <a href="#" id="togglerBullet8">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`square square-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet8">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="square square-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
            <Col sm={6} lg={4} xxl={3}>
              <Card>
                <CardHeader className="code-header">
                  <h5>Plus Bullet</h5>
                  <a href="#" id="togglerBullet9">
                    <IconCode
                      data-source="blockbtn"
                      className="source"
                      size={32}
                    />
                  </a>
                </CardHeader>
                <CardBody>
                  <ul>
                    {bulletVariants.map((variant) => (
                      <li
                        key={variant}
                        className={`plus plus-bullet-${variant}`}
                      >
                        {variant.charAt(0).toUpperCase() + variant.slice(1)}{" "}
                        Bullet
                      </li>
                    ))}
                  </ul>
                  <UncontrolledCollapse toggler="togglerBullet9">
                    <pre tabIndex={0}>
                      <code className="language-html">
                        {`
<ul>  
  ${bulletVariants
    .map(
      (variant) =>
        `<li className="plus plus-bullet-${variant}">${variant.charAt(0).toUpperCase() + variant.slice(1)} Bullet</li>`
    )
    .join("\n  ")}
</ul>
            `}
                      </code>
                    </pre>
                  </UncontrolledCollapse>
                </CardBody>
              </Card>
            </Col>
          </Row>
        </PrismCodeWrapper>
      </Container>
    </div>
  );
};

export default BulletPage;
