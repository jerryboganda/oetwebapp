"use client";
import React, { useEffect, useRef, useState } from "react";
import {
  Button,
  Col,
  Collapse,
  Container,
  Nav,
  NavbarBrand,
  NavbarToggler,
  NavItem,
  NavLink,
  Row,
} from "reactstrap";
import {
  IconArrowNarrowUp,
  IconBrandFigma,
  IconBrandNextjs,
} from "@tabler/icons-react";
import DemoSection from "@/app/other-pages/(landing)/_components/DemoSection";
import Link from "next/link";
import Typed from "typed.js";
import FeatureSection from "@/app/other-pages/(landing)/_components/FeatureSection";
import PurchasePlanSection from "@/app/other-pages/(landing)/_components/PurchasePlanSection";
import FaqContentSection from "@/app/other-pages/(landing)/_components/FaqContentSection";
import LandingFooter from "@/app/other-pages/(landing)/_components/LandingFooter";
import CustomizationOptionSection from "@/app/other-pages/(landing)/_components/CustomizationOptionSection";
import LandingDarkSection from "@/app/other-pages/(landing)/_components/LandingDarkSection";
import ElementSection from "@/app/other-pages/(landing)/_components/ElementSection";
import LandingCardSection from "@/app/other-pages/(landing)/_components/LandingCardSection";
import PreviewBrandImage from "@/Component/CommonElements/PreviewBrandImage";

interface LanguageItem {
  title: string;
  iconClass: React.ReactNode | string;
  link: string;
  bgClass: string;
}

const languageItems: LanguageItem[] = [
  {
    title: "NextJS",
    iconClass: <IconBrandNextjs size={26} className="text-white" />,
    link: "/",
    bgClass: "primary-box bg-primary",
  },
  {
    title: "Figma",
    iconClass: <IconBrandFigma size={26} className="text-white" />,
    link: "https://polytronx.com/design",
    bgClass: "danger-box bg-danger",
  },
];

const LandingPage = () => {
  const [showScrollProgress, setShowScrollProgress] = useState(false);
  const [scrollPercentage, setScrollPercentage] = useState(0);
  const scrollProgressRef = useRef<HTMLDivElement>(null);
  const typedRef = useRef<HTMLSpanElement>(null);
  const typedInstance = useRef<Typed | null>(null);
  const [tooltipVisible, setTooltipVisible] = useState<string | null>(null);

  useEffect(() => {
    if (!typedRef.current) return;

    const options = {
      strings: [
        '<span class="highlight-text">Work<span class="txt-shadow">Work</span></span>',
        '<span class="highlight-text">Goals<span class="txt-shadow">Goals</span></span>',
        '<span class="highlight-text">Projects<span class="txt-shadow">Projects</span></span>',
      ],
      typeSpeed: 100,
      backSpeed: 50,
      loop: true,
    };
    typedInstance.current = new Typed(typedRef.current, options);

    return () => {
      typedInstance.current?.destroy();
    };
  }, []);

  const wrapper: string[] = [
    "Fully Customizable",
    "Google Font",
    "10+ Apps",
    "Fully Customizable",
    "100+ Custom Elements",
    "Mobile Responsive design",
    "150+ Pages",
    "Creative Card Design",
    "4+ Icons Set",
    "Quick Response",
    "Multiple Sidebar Options",
  ];

  const [isOpen, setIsOpen] = useState(false);
  const [isScrolled, setIsScrolled] = useState(false);

  const toggle = () => setIsOpen(!isOpen);

  useEffect(() => {
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 50);
    };

    window.addEventListener("scroll", handleScroll);
    handleScroll();

    return () => {
      window.removeEventListener("scroll", handleScroll);
    };
  }, []);

  useEffect(() => {
    const handleScroll = () => {
      const pos = window.scrollY;
      const calcHeight = document.body.scrollHeight - window.innerHeight;
      const percentage = Math.round((pos * 100) / calcHeight);

      setShowScrollProgress(pos > 100);
      setScrollPercentage(percentage);
    };

    window.addEventListener("scroll", handleScroll);
    handleScroll();

    return () => {
      window.removeEventListener("scroll", handleScroll);
    };
  }, []);

  const [cursorStyle, setCursorStyle] = useState<React.CSSProperties>({});
  const circleRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const mouse = { x: 0, y: 0 };
    const previousMouse = { x: 0, y: 0 };
    const circle = { x: 0, y: 0 };
    let currentScale = 0;
    let currentAngle = 0;
    let animationFrameId: number;

    const handleMouseMove = (e: MouseEvent) => {
      mouse.x = e.clientX;
      mouse.y = e.clientY;
    };

    const speed = 0.17;

    const tick = () => {
      circle.x += (mouse.x - circle.x) * speed;
      circle.y += (mouse.y - circle.y) * speed;

      const deltaMouseX = mouse.x - previousMouse.x;
      const deltaMouseY = mouse.y - previousMouse.y;
      previousMouse.x = mouse.x;
      previousMouse.y = mouse.y;

      const mouseVelocity = Math.min(
        Math.sqrt(deltaMouseX ** 2 + deltaMouseY ** 2) * 4,
        150
      );

      const scaleValue = (mouseVelocity / 150) * 0.5;
      currentScale += (scaleValue - currentScale) * speed;

      const angle = (Math.atan2(deltaMouseY, deltaMouseX) * 180) / Math.PI;
      if (mouseVelocity > 20) currentAngle = angle;
      setCursorStyle({
        transform: `translate(${circle.x}px, ${circle.y}px) 
                   scale(${1 + currentScale}, ${1 - currentScale}) 
                   rotate(${currentAngle}deg)`,
        willChange: "transform",
      });

      animationFrameId = requestAnimationFrame(tick);
    };

    window.addEventListener("mousemove", handleMouseMove);
    const initTimer = setTimeout(tick, 1000);

    return () => {
      window.removeEventListener("mousemove", handleMouseMove);
      clearTimeout(initTimer);
      cancelAnimationFrame(animationFrameId);
    };
  }, []);

  return (
    <div className="landing-page">
      <div ref={circleRef} className="circle-cursor" style={cursorStyle} />

      <div className="landing-wrapper">
        {/*header start*/}
        <div
          className={`navbar navbar-expand-lg sticky-top landing-nav_main px-3 position-fixed w-100 ${
            isScrolled ? "landing-nav-active" : ""
          }`}
        >
          <Container fluid>
            <NavbarBrand className="logo" tag={Link} href="/dashboard/project">
              <img alt="logo" src="/images/logo/polytronx-light.svg" />
            </NavbarBrand>

            <NavbarToggler onClick={toggle} aria-label="Toggle navigation" />

            <Collapse isOpen={isOpen} navbar>
              <Nav className="m-auto" navbar>
                <NavItem>
                  <NavLink className="nav-link active" href="#Demo">
                    Demo
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink className="nav-link" href="#Cards">
                    Cards
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink className="nav-link" href="#Features">
                    Features
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink className="nav-link" href="#Elements">
                    Elements
                  </NavLink>
                </NavItem>
                <NavItem>
                  <NavLink
                    href="mailto:support@polytronx.com"
                    target="_blank"
                    className="nav-link"
                  >
                    Support
                  </NavLink>
                </NavItem>
              </Nav>

              <div className="d-flex">
                <Button
                  color="danger"
                  className="rounded"
                  href="https://forms.gle/hYrBdsJYsqqWe5pKA"
                  target="_blank"
                >
                  Hire Us
                </Button>
                <Button
                  color="primary"
                  className="ms-2 rounded"
                  href="https://polytronx.com"
                  target="_blank"
                >
                  Explore Plans
                </Button>
              </div>
            </Collapse>
          </Container>
        </div>
        {/*header end */}

        {/* landing first section start*/}
        <section className="landing-section p-0">
          <Container fluid>
            <Row className="landing-content">
              {/* Heading Section */}
              <Col lg="6" className="offset-lg-3 position-relative">
                <div className="landing-heading text-center">
                  <h1>
                    Power Up Your <br />
                    <span ref={typedRef} id="highlight-typed"></span> With
                    PolytronX <br />
                  </h1>
                  <img
                    alt="shape"
                    className="img-fluid landing-vector-shape"
                    src="/images/landing/vector-shaps.png"
                  />
                  <p className="text-white">
                    PolytronX includes flexible sidebar options, <br /> RTL
                    layouts, and polished admin essentials.
                  </p>
                  <div className="mt-5">
                    <Button
                      color="primary"
                      className="py-3 px-4 rounded-pill btn-lg"
                      href="/dashboard/project"
                      target="_blank"
                    >
                      Check Now
                    </Button>
                    <Button
                      color="danger"
                      className="py-3 px-4 rounded-pill btn-lg ms-2"
                      href="https://polytronx.com/support"
                      target="_blank"
                    >
                      Docs
                    </Button>
                  </div>
                </div>
              </Col>

              {/* Image Section */}
              <Col xs="12">
                <div className="landing-img">
                  <div className="img-box">
                    <div>
                      <PreviewBrandImage
                        alt="PolytronX workspace preview"
                        className="box-img-1"
                        src="/images/landing/banner-img.gif"
                        badgeClassName="d-inline-block"
                      />
                    </div>
                    <div>
                      <PreviewBrandImage
                        alt="PolytronX dashboard preview"
                        className="box-img-4"
                        src="/images/landing/banner-img-1.gif"
                        badgeClassName="d-inline-block"
                      />
                    </div>
                  </div>
                </div>
              </Col>
            </Row>
          </Container>
        </section>

        {/* landing first section end*/}
      </div>
      <div className="row">
        <div className="language-box d-flex gap-3">
          {languageItems.map((item, index) => (
            <div className="language-box-item" key={index}>
              <a
                href={item.link}
                target="_blank"
                rel="noopener noreferrer"
                className={`${item.bgClass} h-60 w-60 d-flex-center b-r-20`}
                onMouseEnter={() => setTooltipVisible(item.title)}
                onMouseLeave={() => setTooltipVisible(null)}
              >
                {item.iconClass}
                {tooltipVisible === item.title && (
                  <div className="custom-tooltip custom-dark show">
                    {item.title}
                  </div>
                )}
              </a>
            </div>
          ))}
        </div>
      </div>

      {/*demos section start */}
      <section className="demos-section" id="Demo">
        <DemoSection />
      </section>
      {/*demos section end */}

      {/*Features section start */}
      <FeatureSection />
      {/*Features section end */}

      {/*Faq section start */}
      <section className="faq-section">
        <FaqContentSection />
      </section>
      {/*Faq section end */}

      {/*card section start */}
      <section className="card-section" id="Cards">
        <LandingCardSection />
      </section>
      {/*card section end */}

      {/*Element section start */}
      <section className="element-section" id="Elements">
        <ElementSection />
      </section>
      {/*Element section end */}

      {/*Dark Mode section */}
      <section className="dark-section">
        <LandingDarkSection />
      </section>
      {/*Dark Mode section end*/}

      {/*Customization  options section start */}
      <section className="options-section">
        <CustomizationOptionSection />
      </section>
      {/*Customization  options section end */}

      {/*wrapper Section start */}
      <section className="box-wrapper-section p-0">
        <Container fluid className="box-wrapper">
          <ul className="box-wrapper-list">
            {wrapper.map((feature, index) => (
              <li key={index}>{feature}</li>
            ))}
          </ul>
        </Container>
      </section>
      {/*wrapper Section end */}

      {/*Purchase Plans section start  */}
      <section className="plans-section">
        <PurchasePlanSection />
      </section>
      {/*Purchase Plans section end  */}

      {/*footer section start */}
      <section className="landing-footer">
        <LandingFooter />
      </section>
      {/*footer section end */}

      <div
        className="go-top"
        ref={scrollProgressRef}
        style={{
          display: showScrollProgress ? "grid" : "none",
          background: `conic-gradient(
        rgba(var(--primary), 1) ${scrollPercentage}%, 
        var(--light-gray) ${scrollPercentage}%
      )`,
        }}
        onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
      >
        <span className="progress-value">
          <IconArrowNarrowUp size={35} />
        </span>
      </div>
    </div>
  );
};

export default LandingPage;
